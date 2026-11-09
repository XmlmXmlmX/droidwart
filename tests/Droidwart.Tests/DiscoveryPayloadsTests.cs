using System.Text.Json.Nodes;
using Droidwart.Worker.Configuration;
using Droidwart.Worker.Inventory;
using Droidwart.Worker.Mqtt;
using Droidwart.Worker.Updates;
using Xunit;

namespace Droidwart.Tests;

public class DiscoveryPayloadsTests
{
    private static readonly MqttOptions Mqtt = new();

    private static readonly DeviceOptions Garden = new() { Id = "android-ip-cam-garden", Name = "IP Cam Garden", Host = "192.168.178.164" };

    private static readonly AppOptions HaCompanion = new()
    {
        Id = "ha-companion",
        Name = "Home Assistant Companion",
        Repo = "home-assistant/android",
        Policy = UpdatePolicy.Auto,
    };

    private static readonly DeviceInfo Info = new("unknown", "TrebleDroid with GApps", "14", 34, "arm64-v8a", ["arm64-v8a"]);

    [Fact]
    public void UpdateConfig_UsesStableIdsAndTopics()
    {
        var message = DiscoveryPayloads.UpdateConfig(Mqtt, Garden, Info, HaCompanion);
        var json = JsonNode.Parse(message.Payload)!;

        Assert.Equal("homeassistant/update/droidwart_android_ip_cam_garden/ha_companion/config", message.Topic);
        Assert.Equal("droidwart_android_ip_cam_garden_ha_companion", (string?)json["unique_id"]);
        Assert.Equal("update.droidwart_android_ip_cam_garden_ha_companion", (string?)json["default_entity_id"]);
        Assert.Equal("droidwart/android-ip-cam-garden/ha-companion/state", (string?)json["state_topic"]);
        Assert.Equal("Android", (string?)json["device"]!["manufacturer"]);
        Assert.Equal("droidwart/android-ip-cam-garden/ha-companion/install", (string?)json["command_topic"]);
        Assert.Equal("install", (string?)json["payload_install"]);
    }

    [Fact]
    public void NotifyPolicy_HasNoInstallButton()
    {
        var magisk = new AppOptions { Id = "magisk", Name = "Magisk", Repo = "topjohnwu/Magisk", Policy = UpdatePolicy.Notify };

        var json = JsonNode.Parse(DiscoveryPayloads.UpdateConfig(Mqtt, Garden, Info, magisk).Payload)!;

        Assert.Null(json["command_topic"]);
    }

    [Fact]
    public void Progress_IsReportedWhileInstalling_AndResetAfterwards()
    {
        var entry = Entry(null, Check(UpdateState.NotInstalled, "2026.11.1"));

        var running = JsonNode.Parse(DiscoveryPayloads.UpdateState(Mqtt, Garden.Id, entry, inProgress: true, percentage: 45).Payload)!;
        var finished = JsonNode.Parse(DiscoveryPayloads.UpdateState(Mqtt, Garden.Id, entry).Payload)!;

        Assert.True((bool)running["in_progress"]!);
        Assert.Equal(45, (int)running["update_percentage"]!);
        Assert.False((bool)finished["in_progress"]!);
        Assert.Null(finished["update_percentage"]);
    }

    [Fact]
    public void Summary_OverridesReleaseNotes()
    {
        var entry = Entry(null, Check(UpdateState.NotInstalled, "2026.11.1") with { ReleaseNotes = "notes" });

        var json = JsonNode.Parse(DiscoveryPayloads.UpdateState(Mqtt, Garden.Id, entry, summary: "Installation failed: x").Payload)!;

        Assert.Equal("Installation failed: x", (string?)json["release_summary"]);
    }

    [Fact]
    public void DevelopmentBaseTopic_CreatesSeparateEntities()
    {
        var dev = new MqttOptions { BaseTopic = "droidwart-dev" };

        var message = DiscoveryPayloads.UpdateConfig(dev, Garden, Info, HaCompanion);
        var json = JsonNode.Parse(message.Payload)!;

        Assert.Equal("droidwart_dev_android_ip_cam_garden_ha_companion", (string?)json["unique_id"]);
        Assert.StartsWith("homeassistant/update/droidwart_dev_android_ip_cam_garden/", message.Topic);
    }

    [Fact]
    public void UpToDate_ReportsIdenticalVersions_EvenWithFlavorSuffix()
    {
        var entry = Entry("2026.11.1-full", Check(UpdateState.UpToDate, "2026.11.1"));

        var json = JsonNode.Parse(DiscoveryPayloads.UpdateState(Mqtt, Garden.Id, entry).Payload)!;

        Assert.Equal("2026.11.1", (string?)json["installed_version"]);
        Assert.Equal("2026.11.1", (string?)json["latest_version"]);
    }

    [Fact]
    public void UpdateAvailable_ReportsNormalizedVersions()
    {
        var entry = Entry("2026.10.4-full", Check(UpdateState.UpdateAvailable, "2026.11.1"));

        var json = JsonNode.Parse(DiscoveryPayloads.UpdateState(Mqtt, Garden.Id, entry).Payload)!;

        Assert.Equal("2026.10.4", (string?)json["installed_version"]);
        Assert.Equal("2026.11.1", (string?)json["latest_version"]);
    }

    [Fact]
    public void NotInstalled_ReportsPlaceholderAndLatestVersion()
    {
        var entry = Entry(null, Check(UpdateState.NotInstalled, "2026.11.1"));

        var json = JsonNode.Parse(DiscoveryPayloads.UpdateState(Mqtt, Garden.Id, entry).Payload)!;

        Assert.Equal(DiscoveryPayloads.NotInstalled, (string?)json["installed_version"]);
        Assert.Equal("2026.11.1", (string?)json["latest_version"]);
    }

    [Fact]
    public void CheckFailed_OffersNoUpdate()
    {
        var entry = Entry("2026.11.1-full", UpdateCheck.Failed("rate limit"));

        var json = JsonNode.Parse(DiscoveryPayloads.UpdateState(Mqtt, Garden.Id, entry).Payload)!;

        Assert.Equal((string?)json["installed_version"], (string?)json["latest_version"]);
        Assert.Equal("rate limit", (string?)json["release_summary"]);
    }

    [Fact]
    public void ReleaseSummary_IsLimitedTo255Characters()
    {
        var check = Check(UpdateState.UpdateAvailable, "2026.11.1") with { ReleaseNotes = new string('x', 1000) };
        var entry = Entry("2026.10.4-full", check);

        var json = JsonNode.Parse(DiscoveryPayloads.UpdateState(Mqtt, Garden.Id, entry).Payload)!;

        Assert.Equal(255, ((string?)json["release_summary"])!.Length);
    }

    private static UpdateCheck Check(UpdateState state, string version) =>
        new(state, version, version, $"https://github.com/home-assistant/android/releases/tag/{version}", null,
            "app-full-release.apk", "https://example.invalid/app-full-release.apk", 1, null, null, null);

    private static AppInventoryEntry Entry(string? installedVersion, UpdateCheck check)
    {
        var package = installedVersion is null
            ? null
            : new PackageDetails("io.homeassistant.companion.android", installedVersion, 1, 21, 35, null, null, null);

        return new AppInventoryEntry(
            HaCompanion.Id, HaCompanion.Name, HaCompanion.Policy, "io.homeassistant.companion.android",
            package is null ? AppStatus.NotInstalled : AppStatus.Installed, package, null, check, []);
    }
}
