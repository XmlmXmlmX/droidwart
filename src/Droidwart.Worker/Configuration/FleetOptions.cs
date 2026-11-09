namespace Droidwart.Worker.Configuration;

/// <summary>Devices and apps (top-level sections "Devices" and "Apps").</summary>
public sealed class FleetOptions
{
    public List<DeviceOptions> Devices { get; set; } = new();

    public List<AppOptions> Apps { get; set; } = new();

    public IEnumerable<AppOptions> AppsFor(DeviceOptions device) =>
        Apps.Where(a => a.Devices.Count == 0 || a.Devices.Contains(device.Id, StringComparer.Ordinal));

    /// <summary>
    /// Variants of an app for a specific device. A device-level PackageOverrides entry
    /// replaces the package name of the default variant (e.g. a hidden Magisk app).
    /// </summary>
    public static IReadOnlyList<AppVariant> ResolveVariants(AppOptions app, DeviceOptions device)
    {
        if (!device.PackageOverrides.TryGetValue(app.Id, out var overridden) || app.Variants.Count == 0)
        {
            return app.Variants;
        }

        var first = new AppVariant { Package = overridden, AssetPattern = app.Variants[0].AssetPattern };
        return [first, .. app.Variants.Skip(1)];
    }
}
