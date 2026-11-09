namespace Droidwart.Worker.Inventory;

/// <summary>Translates the installer package name into a readable source.</summary>
public static class InstallerNames
{
    private static readonly Dictionary<string, (string Name, bool IsStore)> Known = new(StringComparer.Ordinal)
    {
        ["com.android.vending"] = ("Google Play Store", true),
        ["org.fdroid.fdroid"] = ("F-Droid", true),
        ["org.fdroid.basic"] = ("F-Droid Basic", true),
        ["com.looker.droidify"] = ("Droid-ify", true),
        ["com.machiav3lli.fdroid"] = ("Neo Store", true),
        ["com.aurora.store"] = ("Aurora Store", true),
        ["dev.imranr.obtainium"] = ("Obtainium", false),
        ["dev.imranr.obtainium.fdroid"] = ("Obtainium", false),
        ["com.google.android.packageinstaller"] = ("System package installer (APK file)", false),
        ["com.android.packageinstaller"] = ("System package installer (APK file)", false),
        ["com.android.shell"] = ("ADB", false),
    };

    public static string Describe(string? installer) =>
        installer is null
            ? "unknown (ADB or preinstalled)"
            : Known.TryGetValue(installer, out var known) ? known.Name : installer;

    /// <summary>
    /// Store builds are often signed differently than GitHub builds (e.g. F-Droid vs. GitHub for Termux).
    /// </summary>
    public static bool IsStore(string? installer) =>
        installer is not null && Known.TryGetValue(installer, out var known) && known.IsStore;
}
