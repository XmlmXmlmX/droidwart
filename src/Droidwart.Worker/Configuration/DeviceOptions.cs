namespace Droidwart.Worker.Configuration;

public sealed class DeviceOptions
{
    /// <summary>Technical id, e.g. "android-ip-cam-garden". Becomes part of the MQTT topics later.</summary>
    public string Id { get; set; } = "";

    /// <summary>Display name in Home Assistant.</summary>
    public string Name { get; set; } = "";

    public string Host { get; set; } = "";

    public int Port { get; set; } = 5555;

    /// <summary>Camera name in the Frigate configuration (used by the health check from phase 5).</summary>
    public string? FrigateCamera { get; set; }

    /// <summary>Canary device: receives automatic updates first.</summary>
    public bool Canary { get; set; }

    /// <summary>Package name overrides per app id, e.g. for a hidden Magisk app.</summary>
    public Dictionary<string, string> PackageOverrides { get; set; } = new();

    /// <summary>Serial under which the ADB server lists the device.</summary>
    public string Serial => $"{Host}:{Port}";
}
