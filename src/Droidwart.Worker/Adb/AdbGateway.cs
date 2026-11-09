using System.Net.Sockets;
using AdvancedSharpAdbClient;
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

    public AdbGateway(IOptions<AdbOptions> options, ILogger<AdbGateway> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new AdbClient(_options.ServerHost, _options.ServerPort);
    }

    public async Task<ConnectionState> EnsureConnectedAsync(DeviceOptions device, CancellationToken cancellationToken)
    {
        var state = await GetStateAsync(device, cancellationToken);
        if (state is ConnectionState.Online or ConnectionState.Unauthorized)
        {
            return state;
        }

        _logger.LogInformation("Verbinde {Device} ({Serial}) …", device.Id, device.Serial);
        var response = await WithServerAsync(() => _client.ConnectAsync(device.Host, device.Port, cancellationToken));
        _logger.LogDebug("adb connect {Serial}: {Response}", device.Serial, response.Trim());

        // Der Server meldet ein frisch verbundenes Gerät nicht sofort als "device".
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            state = await GetStateAsync(device, cancellationToken);
            if (state is ConnectionState.Online or ConnectionState.Unauthorized)
            {
                break;
            }
        }

        return state;
    }

    public async Task<ShellResult> ShellAsync(DeviceOptions device, string command, CancellationToken cancellationToken)
    {
        var deviceData = await FindDeviceAsync(device, cancellationToken)
            ?? throw new InvalidOperationException($"Gerät {device.Id} ({device.Serial}) ist nicht verbunden.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.CommandTimeout);

        var receiver = new ConsoleOutputReceiver();
        // ADB-Shell (Protokoll v1) liefert keinen Exit-Code, daher hängen wir ihn als Marker an.
        var wrapped = $"( {command} ); echo {ExitMarker}$?";

        await WithServerAsync(async () =>
        {
            await _client.ExecuteRemoteCommandAsync(wrapped, deviceData, receiver, timeout.Token);
            return true;
        });

        return ParseShellOutput(receiver.ToString());
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
                $"ADB-Server {_options.ServerHost}:{_options.ServerPort} nicht erreichbar: {ex.Message}", ex);
        }
    }
}
