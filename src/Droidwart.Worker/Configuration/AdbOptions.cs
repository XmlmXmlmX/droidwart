namespace Droidwart.Worker.Configuration;

/// <summary>Connection to the ADB server (container "adb-server" in the stack).</summary>
public sealed class AdbOptions
{
    public const string Section = "Adb";

    public string ServerHost { get; set; } = "127.0.0.1";

    public int ServerPort { get; set; } = 5037;

    /// <summary>Maximum runtime of a single shell command on the device.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
