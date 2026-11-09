namespace Droidwart.Worker.Configuration;

public sealed class MqttOptions
{
    public const string Section = "Mqtt";

    /// <summary>Broker host. Empty disables MQTT (e.g. for local development without a broker).</summary>
    public string? Host { get; set; }

    public int Port { get; set; } = 1883;

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string ClientId { get; set; } = "droidwart";

    /// <summary>Root of all Droidwart topics, e.g. "droidwart/android-ip-cam-garden/magisk/state".</summary>
    public string BaseTopic { get; set; } = "droidwart";

    /// <summary>Home Assistant MQTT discovery prefix.</summary>
    public string DiscoveryPrefix { get; set; } = "homeassistant";

    public bool Enabled => !string.IsNullOrWhiteSpace(Host);

    public string StatusTopic => $"{BaseTopic}/status";

    public string AvailabilityTopic(string deviceId) => $"{BaseTopic}/{deviceId}/availability";

    public string UpdateStateTopic(string deviceId, string appId) => $"{BaseTopic}/{deviceId}/{appId}/state";
}
