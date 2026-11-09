using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Droidwart.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace Droidwart.Worker.Inventory;

/// <summary>Writes the inventory to the log in readable form and saves it as JSON in the data directory.</summary>
public sealed class InventoryReporter(IOptions<StorageOptions> storage, ILogger<InventoryReporter> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public void Log(FleetInventory inventory)
    {
        foreach (var device in inventory.Devices)
        {
            logger.LogInformation("=== {Device} ({Name}, {Serial}) – {Connection} ===",
                device.DeviceId, device.Name, device.Serial, device.Connection);

            if (device.Error is not null)
            {
                logger.LogWarning("  {Error}", device.Error);
                continue;
            }

            if (device.Info is { } info)
            {
                logger.LogInformation("  Device: {Manufacturer} {Model}, Android {Android} (SDK {Sdk}), ABI {Abi} [{Abis}], {Bits}",
                    info.Manufacturer, info.Model, info.AndroidVersion, info.Sdk, info.PrimaryAbi,
                    string.Join(", ", info.SupportedAbis), info.Is64Bit ? "64-bit" : "32-bit");
            }

            foreach (var app in device.Apps)
            {
                if (app.Package is { } p)
                {
                    logger.LogInformation("  {App,-14} {Package,-44} {Version} ({Code}) minSdk {MinSdk}, source: {Installer}",
                        app.AppId, p.PackageName, p.VersionName, p.VersionCode, p.MinSdk, InstallerNames.Describe(p.Installer));
                    if (app.LaunchComponent is not null)
                    {
                        logger.LogInformation("  {Blank,-14} Launch component: {Launch}", "", app.LaunchComponent);
                    }
                }
                else
                {
                    logger.LogInformation("  {App,-14} {Package,-44} {Status}",
                        app.AppId, app.ConfiguredPackage ?? "(no package name)", app.Status);
                }

                foreach (var note in app.Notes)
                {
                    logger.LogWarning("  {Blank,-14} ! {Note}", "", note);
                }
            }

            var managed = device.Apps.Select(a => a.Package?.PackageName).OfType<string>().ToHashSet(StringComparer.Ordinal);
            var others = device.ThirdPartyPackages.Where(p => !managed.Contains(p.PackageName)).ToList();
            if (others.Count > 0)
            {
                logger.LogInformation("  Other installed apps ({Count}):", others.Count);
                foreach (var package in others)
                {
                    logger.LogInformation("    {Package,-50} source: {Installer}", package.PackageName, InstallerNames.Describe(package.Installer));
                }
            }
        }
    }

    public async Task SaveAsync(FleetInventory inventory, CancellationToken cancellationToken)
    {
        var directory = storage.Value.DataDirectory;
        Directory.CreateDirectory(directory);

        var target = Path.Combine(directory, "inventory.json");
        var temp = target + ".tmp";

        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, inventory, JsonOptions, cancellationToken);
        }

        File.Move(temp, target, overwrite: true);
        logger.LogInformation("Inventory saved: {Path}", Path.GetFullPath(target));
    }
}
