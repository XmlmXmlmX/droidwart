namespace Droidwart.Worker.Configuration;

/// <summary>Verbindung zum ADB-Server (Container "adb-server" im Stack).</summary>
public sealed class AdbOptions
{
    public const string Section = "Adb";

    public string ServerHost { get; set; } = "127.0.0.1";

    public int ServerPort { get; set; } = 5037;

    /// <summary>Maximale Laufzeit eines einzelnen Shell-Befehls auf dem Gerät.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
