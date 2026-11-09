namespace Droidwart.Worker.Configuration;

public sealed class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>Directory for inventory, APK cache and state. In the container: /data (volume).</summary>
    public string DataDirectory { get; set; } = "data";
}
