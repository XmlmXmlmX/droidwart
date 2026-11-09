namespace Droidwart.Worker.Configuration;

public sealed class DeviceOptions
{
    /// <summary>Technische Id, z. B. "android-ip-cam-garden". Wird später Teil der MQTT-Topics.</summary>
    public string Id { get; set; } = "";

    /// <summary>Anzeigename in Home Assistant.</summary>
    public string Name { get; set; } = "";

    public string Host { get; set; } = "";

    public int Port { get; set; } = 5555;

    /// <summary>Name der Kamera in der Frigate-Konfiguration (für den Health-Check ab Phase 5).</summary>
    public string? FrigateCamera { get; set; }

    /// <summary>Canary-Gerät: bekommt automatische Updates zuerst.</summary>
    public bool Canary { get; set; }

    /// <summary>Abweichende Paketnamen je App-Id, z. B. für eine versteckte Magisk-App.</summary>
    public Dictionary<string, string> PackageOverrides { get; set; } = new();

    /// <summary>Seriennummer, unter der der ADB-Server das Gerät führt.</summary>
    public string Serial => $"{Host}:{Port}";
}
