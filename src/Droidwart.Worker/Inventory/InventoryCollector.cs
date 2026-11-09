using System.Diagnostics;
using Droidwart.Worker.Adb;
using Droidwart.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace Droidwart.Worker.Inventory;

/// <summary>Liest pro Gerät Systeminfos, die konfigurierten Apps und alle Drittanbieter-Apps.</summary>
public sealed class InventoryCollector(
    IAdbGateway adb,
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
        var devices = new List<DeviceInventory>();

        foreach (var device in options.Devices)
        {
            devices.Add(await CollectDeviceAsync(device, options, cancellationToken));
        }

        return new FleetInventory(time.GetUtcNow(), devices);
    }

    private async Task<DeviceInventory> CollectDeviceAsync(DeviceOptions device, FleetOptions options, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("inventory device");
        activity?.SetTag("droidwart.device", device.Id);

        var result = await CollectDeviceCoreAsync(device, options, cancellationToken);

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

    private async Task<DeviceInventory> CollectDeviceCoreAsync(DeviceOptions device, FleetOptions options, CancellationToken cancellationToken)
    {
        ConnectionState state;
        try
        {
            state = await adb.EnsureConnectedAsync(device, cancellationToken);
        }
        catch (AdbServerUnavailableException ex)
        {
            return DeviceInventory.Failed(device.Id, device.Name, device.Serial, ConnectionState.Unreachable, ex.Message);
        }

        if (state != ConnectionState.Online)
        {
            var reason = state switch
            {
                ConnectionState.Unauthorized =>
                    "ADB-Schlüssel nicht autorisiert. adbkey.pub des ADB-Servers auf dem Gerät freigeben.",
                ConnectionState.Offline =>
                    "Gerät meldet sich als offline. ADB über TCP auf dem Gerät neu starten oder Gerät neu booten.",
                _ => "Gerät nicht erreichbar. IP, Port 5555 und das Magisk-Boot-Skript prüfen.",
            };
            return DeviceInventory.Failed(device.Id, device.Name, device.Serial, state, reason);
        }

        try
        {
            var info = await ReadDeviceInfoAsync(device, cancellationToken);
            var thirdParty = await ReadThirdPartyPackagesAsync(device, cancellationToken);

            var apps = new List<AppInventoryEntry>();
            foreach (var app in options.AppsFor(device))
            {
                apps.Add(await CollectAppAsync(device, app, info, cancellationToken));
            }

            return new DeviceInventory(device.Id, device.Name, device.Serial, state, null, info, apps, thirdParty);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Inventar für {Device} unvollständig", device.Id);
            return DeviceInventory.Failed(device.Id, device.Name, device.Serial, state, $"Fehler beim Auslesen: {ex.Message}");
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

    private async Task<List<InstalledPackage>> ReadThirdPartyPackagesAsync(DeviceOptions device, CancellationToken cancellationToken)
    {
        var result = await adb.ShellAsync(device, "pm list packages -3 -i", cancellationToken);
        return AndroidParsers.ParsePmListPackages(result.Output);
    }

    private async Task<AppInventoryEntry> CollectAppAsync(
        DeviceOptions device, AppOptions app, DeviceInfo info, CancellationToken cancellationToken)
    {
        var configured = FleetOptions.ResolvePackage(app, device);
        var notes = new List<string>();

        if (string.IsNullOrWhiteSpace(configured))
        {
            notes.Add("Kein Paketname konfiguriert. Den passenden Eintrag aus 'Weitere installierte Apps' übernehmen.");
            return new AppInventoryEntry(app.Id, app.Name, null, AppStatus.PackageUnknown, null, null, notes);
        }

        // Paketnamen sind durch den FleetOptionsValidator auf [A-Za-z0-9_.] beschränkt,
        // daher ist das Einsetzen in den Shell-Befehl unkritisch.
        PackageDetails? found = null;
        foreach (var candidate in new[] { configured }.Concat(app.AlternativePackages))
        {
            var dump = await adb.ShellAsync(device, $"dumpsys package {candidate}", cancellationToken);
            found = AndroidParsers.ParseDumpsysPackage(dump.Output, candidate);
            if (found is not null)
            {
                break;
            }
        }

        if (found is null)
        {
            if (app.NotInstalledHint is not null)
            {
                notes.Add(app.NotInstalledHint);
            }

            return new AppInventoryEntry(app.Id, app.Name, configured, AppStatus.NotInstalled, null, null, notes);
        }

        if (!string.Equals(found.PackageName, configured, StringComparison.Ordinal))
        {
            notes.Add($"Installiert ist die Variante {found.PackageName} statt {configured}. Asset-Auswahl muss dazu passen.");
        }

        if (app.RequireInstallSource is not null && InstallerNames.IsStore(found.Installer))
        {
            notes.Add($"Installiert über {InstallerNames.Describe(found.Installer)}. " +
                      $"Updates aus GitHub ({app.Repo}) scheitern vermutlich an einer anderen Signatur.");
        }

        if (info.Sdk is { } sdk && found.TargetSdk is { } target && target > sdk)
        {
            notes.Add($"targetSdk {target} liegt über dem Geräte-SDK {sdk}.");
        }

        var launch = app.LaunchComponent ?? await ResolveLaunchComponentAsync(device, found.PackageName, cancellationToken);

        return new AppInventoryEntry(app.Id, app.Name, configured, AppStatus.Installed, found, launch, notes);
    }

    private async Task<string?> ResolveLaunchComponentAsync(DeviceOptions device, string package, CancellationToken cancellationToken)
    {
        // "cmd package resolve-activity" gibt es ab Android 7. Auf älteren Geräten bleibt das Feld leer.
        var result = await adb.ShellAsync(device, $"cmd package resolve-activity --brief {package}", cancellationToken);
        return result.Success ? AndroidParsers.ParseResolveActivity(result.Output) : null;
    }
}
