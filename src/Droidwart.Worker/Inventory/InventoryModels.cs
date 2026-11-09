using Droidwart.Worker.Adb;
using Droidwart.Worker.Configuration;
using Droidwart.Worker.Updates;

namespace Droidwart.Worker.Inventory;

public sealed record FleetInventory(DateTimeOffset CollectedAt, IReadOnlyList<DeviceInventory> Devices);

public sealed record DeviceInventory(
    string DeviceId,
    string Name,
    string Serial,
    ConnectionState Connection,
    string? Error,
    DeviceInfo? Info,
    IReadOnlyList<AppInventoryEntry> Apps,
    IReadOnlyList<InstalledPackage> ThirdPartyPackages,
    IReadOnlyList<string> Warnings)
{
    public static DeviceInventory Failed(string id, string name, string serial, ConnectionState state, string error) =>
        new(id, name, serial, state, error, null, [], [], []);
}

public sealed record DeviceInfo(
    string Manufacturer,
    string Model,
    string AndroidVersion,
    int? Sdk,
    string PrimaryAbi,
    IReadOnlyList<string> SupportedAbis)
{
    public bool Is64Bit => SupportedAbis.Contains("arm64-v8a") || SupportedAbis.Contains("x86_64");
}

public enum AppStatus
{
    Installed,
    NotInstalled,
}

public sealed record AppInventoryEntry(
    string AppId,
    string Name,
    UpdatePolicy Policy,
    string VariantPackage,
    AppStatus Status,
    PackageDetails? Package,
    string? LaunchComponent,
    UpdateCheck Update,
    IReadOnlyList<string> Notes);

public sealed record PackageDetails(
    string PackageName,
    string? VersionName,
    long? VersionCode,
    int? MinSdk,
    int? TargetSdk,
    string? Installer,
    string? FirstInstallTime,
    string? LastUpdateTime);

public sealed record InstalledPackage(string PackageName, string? Installer);
