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

public sealed class AppOptions
{
    /// <summary>Technical id, e.g. "ha-companion". Becomes part of the MQTT topics later.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>GitHub repository in the format "owner/name".</summary>
    public string Repo { get; set; } = "";

    /// <summary>Expected package name. Leave empty if unknown; the inventory then lists candidates.</summary>
    public string? Package { get; set; }

    /// <summary>Other package names of the same app (e.g. flavors such as "minimal" or "fdroid").</summary>
    public List<string> AlternativePackages { get; set; } = new();

    /// <summary>Regex for the release asset. The placeholder {abi} is replaced per device.</summary>
    public string AssetPattern { get; set; } = @"\.apk$";

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
