using System.Diagnostics;
using Droidwart.Worker.Adb;
using Droidwart.Worker.Configuration;
using Droidwart.Worker.GitHub;
using Droidwart.Worker.Updates;
using Microsoft.Extensions.Options;

namespace Droidwart.Worker.Inventory;

/// <summary>
/// Reads system info, the configured apps and all third-party apps for each device
/// and compares the installed versions with the latest GitHub releases.
/// </summary>
public sealed class InventoryCollector(
    IAdbGateway adb,
    GitHubReleaseClient gitHub,
    IOptionsMonitor<FleetOptions> fleet,
    TimeProvider time,
    ILogger<InventoryCollector> logger)
{
    private static readonly string[] Properties =
    [
        "ro.product.manufacturer",
        "ro.product.model",
        "ro.build.version.release",
        "ro.build.version.sdk",
        "ro.product.cpu.abi",
        "ro.product.cpu.abilist",
    ];

    public async Task<FleetInventory> CollectAsync(CancellationToken cancellationToken)
    {
        var options = fleet.CurrentValue;

        // One lookup per app, shared by all devices.
        var releases = new Dictionary<string, ReleaseLookup>(StringComparer.Ordinal);
        foreach (var app in options.Apps)
        {
            releases[app.Id] = await gitHub.GetLatestAsync(app.Repo, app.IncludePrereleases, cancellationToken);
        }

        var devices = new List<DeviceInventory>();
        foreach (var device in options.Devices)
        {
            devices.Add(await CollectDeviceAsync(device, options, releases, cancellationToken));
        }

        return new FleetInventory(time.GetUtcNow(), devices);
    }

    private async Task<DeviceInventory> CollectDeviceAsync(
        DeviceOptions device, FleetOptions options, IReadOnlyDictionary<string, ReleaseLookup> releases, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("inventory device");
        activity?.SetTag("droidwart.device", device.Id);

        var result = await CollectDeviceCoreAsync(device, options, releases, cancellationToken);

        activity?.SetTag("droidwart.connection", result.Connection.ToString());
        if (result.Error is not null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, result.Error);
        }

        Telemetry.DeviceInventories.Add(1,
            new KeyValuePair<string, object?>("device", device.Id),
            new KeyValuePair<string, object?>("connection", result.Connection.ToString()));

        return result;
    }

    private async Task<DeviceInventory> CollectDeviceCoreAsync(
        DeviceOptions device, FleetOptions options, IReadOnlyDictionary<string, ReleaseLookup> releases, CancellationToken cancellationToken)
    {
        ConnectionResult connection;
        try
        {
            connection = await adb.EnsureConnectedAsync(device, cancellationToken);
        }
        catch (AdbServerUnavailableException ex)
        {
            return DeviceInventory.Failed(device.Id, device.Name, device.Serial, ConnectionState.Unreachable, ex.Message);
        }

        if (connection.State != ConnectionState.Online)
        {
            var reason = connection.State switch
            {
                ConnectionState.Unauthorized =>
                    "ADB key not authorized. Authorize the ADB server's adbkey.pub on the device.",
                ConnectionState.Offline =>
                    "Device reports as offline. Restart ADB over TCP on the device or reboot it.",
                _ => "Device not reachable. Check the IP, port 5555 and the Magisk boot script.",
            };

            if (connection.Detail is not null)
            {
                reason += $" ADB server: {connection.Detail}";
            }

            return DeviceInventory.Failed(device.Id, device.Name, device.Serial, connection.State, reason);
        }

        try
        {
            var warnings = new List<string>();
            var info = await ReadDeviceInfoAsync(device, cancellationToken);
            var thirdParty = await ReadThirdPartyPackagesAsync(device, warnings, cancellationToken);

            var apps = new List<AppInventoryEntry>();
            foreach (var app in options.AppsFor(device))
            {
                apps.Add(await CollectAppAsync(device, app, info, releases[app.Id], cancellationToken));
            }

            return new DeviceInventory(device.Id, device.Name, device.Serial, connection.State, null, info, apps, thirdParty, warnings);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Inventory for {Device} is incomplete", device.Id);
            return DeviceInventory.Failed(device.Id, device.Name, device.Serial, connection.State, $"Failed to read device: {ex.Message}");
        }
    }

    private async Task<DeviceInfo> ReadDeviceInfoAsync(DeviceOptions device, CancellationToken cancellationToken)
    {
        var command = $"for p in {string.Join(' ', Properties)}; do echo \"$p=$(getprop $p)\"; done";
        var result = await adb.ShellAsync(device, command, cancellationToken);
        var props = AndroidParsers.ParseKeyValueLines(result.Output);

        string Get(string key) => props.TryGetValue(key, out var value) ? value : "";

        var abiList = Get("ro.product.cpu.abilist")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var primaryAbi = Get("ro.product.cpu.abi");

        return new DeviceInfo(
            Get("ro.product.manufacturer"),
            Get("ro.product.model"),
            Get("ro.build.version.release"),
            int.TryParse(Get("ro.build.version.sdk"), out var sdk) ? sdk : null,
            primaryAbi,
            abiList.Length > 0 ? abiList : new[] { primaryAbi });
    }

    private async Task<List<InstalledPackage>> ReadThirdPartyPackagesAsync(
        DeviceOptions device, List<string> warnings, CancellationToken cancellationToken)
    {
        var result = await adb.ShellAsync(device, "pm list packages -3 -i", cancellationToken);
        if (!result.Success)
        {
            warnings.Add($"'pm list packages' failed (exit code {result.ExitCode?.ToString() ?? "unknown"}): {result.Output}");
        }

        return AndroidParsers.ParsePmListPackages(result.Output);
    }

    private async Task<AppInventoryEntry> CollectAppAsync(
        DeviceOptions device, AppOptions app, DeviceInfo info, ReleaseLookup release, CancellationToken cancellationToken)
    {
        var variants = FleetOptions.ResolveVariants(app, device);
        var notes = new List<string>();

        // Package names are restricted to [A-Za-z0-9_.] by FleetOptionsValidator,
        // so inserting them into the shell command is safe.
        AppVariant? installedVariant = null;
        PackageDetails? found = null;
        foreach (var variant in variants)
        {
            var dump = await adb.ShellAsync(device, $"dumpsys package {variant.Package}", cancellationToken);
            found = AndroidParsers.ParseDumpsysPackage(dump.Output, variant.Package);
            if (found is not null)
            {
                installedVariant = variant;
                break;
            }
        }

        // Fresh installs use the default (first) variant.
        var effectiveVariant = installedVariant ?? variants[0];
        var update = UpdateEvaluator.Evaluate(effectiveVariant, info.SupportedAbis, found, release);

        if (found is null)
        {
            if (app.NotInstalledHint is not null)
            {
                notes.Add(app.NotInstalledHint);
            }

            return new AppInventoryEntry(app.Id, app.Name, app.Policy, effectiveVariant.Package, AppStatus.NotInstalled,
                null, null, update, notes);
        }

        if (!ReferenceEquals(effectiveVariant, variants[0]))
        {
            notes.Add($"Installed variant: {found.PackageName}.");
        }

        if (app.RequireInstallSource is not null && InstallerNames.IsStore(found.Installer))
        {
            notes.Add($"Installed via {InstallerNames.Describe(found.Installer)}. " +
                      $"Updates from GitHub ({app.Repo}) will likely fail due to a different signature.");
        }

        var launch = app.LaunchComponent ?? await ResolveLaunchComponentAsync(device, found.PackageName, cancellationToken);

        return new AppInventoryEntry(app.Id, app.Name, app.Policy, effectiveVariant.Package, AppStatus.Installed,
            found, launch, update, notes);
    }

    private async Task<string?> ResolveLaunchComponentAsync(DeviceOptions device, string package, CancellationToken cancellationToken)
    {
        // "cmd package resolve-activity" exists since Android 7. On older devices the field stays empty.
        var result = await adb.ShellAsync(device, $"cmd package resolve-activity --brief {package}", cancellationToken);
        return result.Success ? AndroidParsers.ParseResolveActivity(result.Output) : null;
    }
}
