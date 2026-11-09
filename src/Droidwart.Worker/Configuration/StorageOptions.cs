namespace Droidwart.Worker.Configuration;

public sealed class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>Verzeichnis für Inventar, APK-Cache und Zustand. Im Container: /data (Volume).</summary>
    public string DataDirectory { get; set; } = "data";
}
