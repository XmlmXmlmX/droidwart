using System.Reflection;
using System.Text.Json.Nodes;
using Droidwart.Worker.Configuration;
using Droidwart.Worker.Inventory;
using Droidwart.Worker.Updates;

using WorkerUpdateState = Droidwart.Worker.Updates.UpdateState;

namespace Droidwart.Worker.Mqtt;

public sealed record MqttMessage(string Topic, string Payload);

/// <summary>Builds the Home Assistant MQTT discovery and state payloads.</summary>
public static class DiscoveryPayloads
{
    public const string NotInstalled = "not installed";

    private const int MaxReleaseSummaryLength = 255;

    private static readonly string Version =
        typeof(DiscoveryPayloads).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    /// <summary>Converts an id like "android-ip-cam-garden" to "android_ip_cam_garden" for entity ids.</summary>
    public static string Slug(string id) => id.Replace('-', '_');

    public static MqttMessage UpdateConfig(MqttOptions mqtt, DeviceOptions device, DeviceInfo? info, AppOptions app)
    {
        var node = NodeId(mqtt, device);
        var uniqueId = $"{node}_{Slug(app.Id)}";

        var config = new JsonObject
        {
            ["name"] = app.Name,
            ["unique_id"] = uniqueId,
            ["default_entity_id"] = $"update.{uniqueId}",
            ["state_topic"] = mqtt.UpdateStateTopic(device.Id, app.Id),
            ["availability"] = Availability(mqtt, device.Id),
            ["availability_mode"] = "all",
            ["device"] = Device(node, device, info),
            ["origin"] = Origin(),
        };

        // Phase 3 only reports versions. The install command (command_topic) follows in phase 4.

        return new MqttMessage(
            $"{mqtt.DiscoveryPrefix}/update/{node}/{Slug(app.Id)}/config",
            config.ToJsonString());
    }

    public static MqttMessage ConnectivityConfig(MqttOptions mqtt, DeviceOptions device, DeviceInfo? info)
    {
        var node = NodeId(mqtt, device);
        var uniqueId = $"{node}_adb";

        var config = new JsonObject
        {
            ["name"] = "ADB connection",
            ["unique_id"] = uniqueId,
            ["default_entity_id"] = $"binary_sensor.{uniqueId}",
            ["device_class"] = "connectivity",
            ["entity_category"] = "diagnostic",
            ["state_topic"] = mqtt.AvailabilityTopic(device.Id),
            ["payload_on"] = "online",
            ["payload_off"] = "offline",
            // Only depends on Droidwart itself, so it can show "disconnected" while the phone is offline.
            ["availability_topic"] = mqtt.StatusTopic,
            ["device"] = Device(node, device, info),
            ["origin"] = Origin(),
        };

        return new MqttMessage(
            $"{mqtt.DiscoveryPrefix}/binary_sensor/{node}/adb/config",
            config.ToJsonString());
    }

    /// <summary>
    /// State of an update entity. Home Assistant compares the versions itself, so Droidwart reports
    /// normalized versions and identical strings whenever it considers the app up to date.
    /// Otherwise "2026.11.1-full" vs. "2026.11.1" would show up as an update.
    /// </summary>
    public static MqttMessage UpdateState(MqttOptions mqtt, string deviceId, AppInventoryEntry app)
    {
        var update = app.Update;
        var installedRaw = app.Package?.VersionName;
        var installed = VersionComparer.Normalize(installedRaw) ?? installedRaw;

        string? installedVersion;
        string? latestVersion;

        switch (update.State)
        {
            case WorkerUpdateState.NotInstalled:
                installedVersion = NotInstalled;
                latestVersion = update.LatestVersion ?? update.LatestTag;
                break;
            case WorkerUpdateState.UpdateAvailable:
                installedVersion = installed;
                latestVersion = update.LatestVersion ?? update.LatestTag;
                break;
            case WorkerUpdateState.Unknown:
                installedVersion = installedRaw;
                latestVersion = update.LatestTag;
                break;
            default:
                // UpToDate, InstalledNewer, CheckFailed: no update offered.
                installedVersion = installed ?? NotInstalled;
                latestVersion = installedVersion;
                break;
        }

        var state = new JsonObject
        {
            ["installed_version"] = installedVersion,
            ["latest_version"] = latestVersion,
            ["title"] = app.Name,
            ["release_summary"] = ReleaseSummary(app),
            ["release_url"] = update.ReleaseUrl,
            ["in_progress"] = false,
        };

        return new MqttMessage(mqtt.UpdateStateTopic(deviceId, app.AppId), state.ToJsonString());
    }

    private static string? ReleaseSummary(AppInventoryEntry app)
    {
        var text = app.Update.Problem
                   ?? app.Notes.FirstOrDefault()
                   ?? (app.Policy == UpdatePolicy.Notify ? "Update manually on the device (policy: Notify)." : null)
                   ?? app.Update.ReleaseNotes;

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = text.Trim();
        return text.Length <= MaxReleaseSummaryLength ? text : text[..(MaxReleaseSummaryLength - 1)] + "…";
    }

    private static JsonArray Availability(MqttOptions mqtt, string deviceId) =>
        new(
            new JsonObject { ["topic"] = mqtt.StatusTopic },
            new JsonObject { ["topic"] = mqtt.AvailabilityTopic(deviceId) });

    /// <summary>
    /// Node id per device, derived from the base topic, so a development instance with another
    /// base topic ("droidwart-dev") creates separate entities instead of overwriting production ones.
    /// </summary>
    public static string NodeId(MqttOptions mqtt, DeviceOptions device) => $"{Slug(mqtt.BaseTopic)}_{Slug(device.Id)}";

    private static JsonObject Device(string node, DeviceOptions device, DeviceInfo? info)
    {
        var json = new JsonObject
        {
            ["identifiers"] = new JsonArray(node),
            ["name"] = device.Name,
        };

        if (info is not null)
        {
            json["manufacturer"] = string.IsNullOrWhiteSpace(info.Manufacturer) || info.Manufacturer == "unknown"
                ? "Android"
                : info.Manufacturer;
            json["model"] = info.Model;
            json["sw_version"] = $"Android {info.AndroidVersion}";
        }

        return json;
    }

    private static JsonObject Origin() => new()
    {
        ["name"] = "Droidwart",
        ["sw_version"] = Version,
        ["support_url"] = "https://github.com/XmlmXmlmX/droidwart",
    };
}
