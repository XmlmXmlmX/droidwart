using System.Collections.Concurrent;
using System.Net.Sockets;
using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Exceptions;
using AdvancedSharpAdbClient.Models;
using AdvancedSharpAdbClient.Receivers;
using Droidwart.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace Droidwart.Worker.Adb;

public sealed class AdbGateway : IAdbGateway
{
    private const string ExitMarker = "__DROIDWART_EXIT__";

    private readonly AdbClient _client;
    private readonly AdbOptions _options;
    private readonly ILogger<AdbGateway> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _connectLocks = new(StringComparer.Ordinal);

    public AdbGateway(IOptions<AdbOptions> options, ILogger<AdbGateway> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new AdbClient(_options.ServerHost, _options.ServerPort);
    }

    public async Task<ConnectionResult> EnsureConnectedAsync(DeviceOptions device, CancellationToken cancellationToken)
    {
        // Inventory and connection monitor may connect the same device at the same time.
        var connectLock = _connectLocks.GetOrAdd(device.Id, _ => new SemaphoreSlim(1, 1));
        await connectLock.WaitAsync(cancellationToken);
        try
        {
            return await EnsureConnectedCoreAsync(device, cancellationToken);
        }
        finally
        {
            connectLock.Release();
        }
    }

    private async Task<ConnectionResult> EnsureConnectedCoreAsync(DeviceOptions device, CancellationToken cancellationToken)
    {
        var state = await GetStateAsync(device, cancellationToken);
        if (state is ConnectionState.Online or ConnectionState.Unauthorized)
        {
            return new ConnectionResult(state);
        }

        _logger.LogInformation("Connecting {Device} ({Serial})…", device.Id, device.Serial);

        string response;
        try
        {
            response = (await WithServerAsync(() => _client.ConnectAsync(device.Host, device.Port, cancellationToken))).Trim();
        }
        catch (AdbException ex)
        {
            response = ex.Message;
        }

        // The server does not report a freshly connected device as "device" right away.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            state = await GetStateAsync(device, cancellationToken);
            if (state is ConnectionState.Online or ConnectionState.Unauthorized)
            {
                break;
            }
        }

        if (state is ConnectionState.Online)
        {
            _logger.LogInformation("Connected {Device}: {Response}", device.Id, response);
            return new ConnectionResult(state);
        }

        _logger.LogWarning("Could not connect {Device} ({Serial}): {Response}", device.Id, device.Serial, response);
        return new ConnectionResult(state, response);
    }

    public async Task<ShellResult> ShellAsync(DeviceOptions device, string command, CancellationToken cancellationToken)
    {
        var deviceData = await FindDeviceAsync(device, cancellationToken)
            ?? throw new InvalidOperationException($"Device {device.Id} ({device.Serial}) is not connected.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.CommandTimeout);

        var receiver = new ConsoleOutputReceiver();
        // The ADB shell (protocol v1) does not return an exit code, so we append it as a marker.
        var wrapped = $"( {command} ); echo {ExitMarker}$?";

        await WithServerAsync(async () =>
        {
            await _client.ExecuteRemoteCommandAsync(wrapped, deviceData, receiver, timeout.Token);
            return true;
        });

        return ParseShellOutput(receiver.ToString());
    }

    public async Task InstallAsync(DeviceOptions device, string apkPath, IReadOnlyList<string> arguments, Action<double> uploadProgress,
        CancellationToken cancellationToken)
    {
        var deviceData = await FindDeviceAsync(device, cancellationToken)
            ?? throw new InvalidOperationException($"Device {device.Id} ({device.Serial}) is not connected.");

        // Large APKs over Wi-Fi take a while; the regular shell timeout is far too short.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));

        await using var apk = File.OpenRead(apkPath);
        try
        {
            await WithServerAsync(async () =>
            {
                await _client.InstallAsync(
                    deviceData,
                    apk,
                    e =>
                    {
                        if (e.State == PackageInstallProgressState.Uploading)
                        {
                            uploadProgress(e.UploadProgress);
                        }
                    },
                    timeout.Token,
                    [.. arguments]);
                return true;
            });
        }
        catch (AdbException ex)
        {
            // e.g. "Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE: ...]"
            throw new InvalidOperationException(ex.Message.Trim(), ex);
        }
    }

    public static ShellResult ParseShellOutput(string raw)
    {
        var index = raw.LastIndexOf(ExitMarker, StringComparison.Ordinal);
        if (index < 0)
        {
            return new ShellResult(raw.TrimEnd(), null);
        }

        var output = raw[..index].TrimEnd();
        var codeText = raw[(index + ExitMarker.Length)..].Trim();
        return new ShellResult(output, int.TryParse(codeText, out var code) ? code : null);
    }

    private async Task<ConnectionState> GetStateAsync(DeviceOptions device, CancellationToken cancellationToken)
    {
        var deviceData = await FindDeviceAsync(device, cancellationToken, onlyOnline: false);
        return deviceData?.State switch
        {
            null => ConnectionState.Unreachable,
            DeviceState.Online => ConnectionState.Online,
            DeviceState.Unauthorized => ConnectionState.Unauthorized,
            _ => ConnectionState.Offline,
        };
    }

    private async Task<DeviceData?> FindDeviceAsync(DeviceOptions device, CancellationToken cancellationToken, bool onlyOnline = true)
    {
        var devices = await WithServerAsync(() => _client.GetDevicesAsync(cancellationToken));
        var match = devices.FirstOrDefault(d => string.Equals(d.Serial, device.Serial, StringComparison.Ordinal));
        return match is not null && (!onlyOnline || match.State == DeviceState.Online) ? match : null;
    }

    private async Task<T> WithServerAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (SocketException ex)
        {
            throw new AdbServerUnavailableException(
                $"ADB server {_options.ServerHost}:{_options.ServerPort} is not reachable: {ex.Message}", ex);
        }
    }
}
