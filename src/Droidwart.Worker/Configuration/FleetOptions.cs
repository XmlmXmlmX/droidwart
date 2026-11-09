namespace Droidwart.Worker.Configuration;

/// <summary>Devices and apps (top-level sections "Devices" and "Apps").</summary>
public sealed class FleetOptions
{
    public List<DeviceOptions> Devices { get; set; } = new();

    public List<AppOptions> Apps { get; set; } = new();

    public IEnumerable<AppOptions> AppsFor(DeviceOptions device) =>
        Apps.Where(a => a.Devices.Count == 0 || a.Devices.Contains(device.Id, StringComparer.Ordinal));

    public static string? ResolvePackage(AppOptions app, DeviceOptions device) =>
        device.PackageOverrides.TryGetValue(app.Id, out var overridden) ? overridden : app.Package;
}
