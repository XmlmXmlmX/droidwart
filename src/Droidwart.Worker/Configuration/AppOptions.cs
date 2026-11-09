namespace Droidwart.Worker.Configuration;

public enum UpdatePolicy
{
    /// <summary>Only report new versions; installing through Droidwart is disabled.</summary>
    Notify,

    /// <summary>Install only when triggered from Home Assistant.</summary>
    Manual,

    /// <summary>Install automatically within the maintenance window (canary first).</summary>
    Auto,
}

/// <summary>
/// One flavor of an app: a package name and the release asset that belongs to it,
/// e.g. "full" vs. "minimal" for the Home Assistant Companion app.
/// </summary>
public sealed class AppVariant
{
    public string Package { get; set; } = "";

    /// <summary>Regex for the release asset. The placeholder {abi} is replaced with each supported ABI of the device.</summary>
    public string AssetPattern { get; set; } = @"\.apk$";
}

public sealed class AppOptions
{
    /// <summary>Technical id, e.g. "ha-companion". Becomes part of the MQTT topics later.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>GitHub repository in the format "owner/name".</summary>
    public string Repo { get; set; } = "";

    /// <summary>
    /// Known variants. The first one is the default and is used for fresh installs;
    /// for installed apps the variant matching the installed package is used.
    /// </summary>
    public List<AppVariant> Variants { get; set; } = new();

    public bool IncludePrereleases { get; set; }

    public UpdatePolicy Policy { get; set; } = UpdatePolicy.Manual;

    /// <summary>If set (e.g. "github"), a warning is issued when the app was installed from a store.</summary>
    public string? RequireInstallSource { get; set; }

    /// <summary>Component to relaunch after an update ("package/.Activity"). Empty = resolve automatically.</summary>
    public string? LaunchComponent { get; set; }

    /// <summary>Hint shown in the inventory when the app is not found.</summary>
    public string? NotInstalledHint { get; set; }

    /// <summary>Device ids on which the app is managed. Empty = all devices.</summary>
    public List<string> Devices { get; set; } = new();
}
