namespace Droidwart.Worker.Inventory;

/// <summary>Übersetzt den Installer-Paketnamen in eine lesbare Quelle.</summary>
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
        ["com.google.android.packageinstaller"] = ("System-Paketinstaller (APK-Datei)", false),
        ["com.android.packageinstaller"] = ("System-Paketinstaller (APK-Datei)", false),
        ["com.android.shell"] = ("ADB", false),
    };

    public static string Describe(string? installer) =>
        installer is null
            ? "unbekannt (ADB oder vorinstalliert)"
            : Known.TryGetValue(installer, out var known) ? known.Name : installer;

    /// <summary>
    /// Store-Builds sind oft anders signiert als GitHub-Builds (z. B. F-Droid vs. GitHub bei Termux).
    /// </summary>
    public static bool IsStore(string? installer) =>
        installer is not null && Known.TryGetValue(installer, out var known) && known.IsStore;
}
