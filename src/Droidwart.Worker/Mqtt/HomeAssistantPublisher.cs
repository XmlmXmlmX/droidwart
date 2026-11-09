using System.Collections.Concurrent;
using Droidwart.Worker.Adb;
using Droidwart.Worker.Configuration;
using Droidwart.Worker.Inventory;
using Microsoft.Extensions.Options;

namespace Droidwart.Worker.Mqtt;

/// <summary>
/// Publishes discovery, availability and update states to Home Assistant.
/// Everything is retained, so Home Assistant gets the current state after its own restarts as well.
/// After a broker reconnect the last known state is published again.
/// </summary>
public sealed class HomeAssistantPublisher
{
    private readonly MqttConnection _mqtt;
    private readonly MqttOptions _options;
    private readonly IOptionsMonitor<FleetOptions> _fleet;
    private readonly ILogger<HomeAssistantPublisher> _logger;
    private readonly ConcurrentDictionary<string, bool> _availability = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _publishLock = new(1, 1);
    private FleetInventory? _lastInventory;

    public HomeAssistantPublisher(
        MqttConnection mqtt, IOptions<MqttOptions> options, IOptionsMonitor<FleetOptions> fleet, ILogger<HomeAssistantPublisher> logger)
    {
        _mqtt = mqtt;
        _options = options.Value;
        _fleet = fleet;
        _logger = logger;
        _mqtt.Connected += RepublishAsync;
    }

    public async Task PublishInventoryAsync(FleetInventory inventory, CancellationToken cancellationToken)
    {
        _lastInventory = inventory;
        if (!_mqtt.IsConnected)
        {
            _logger.LogInformation("MQTT not connected; the inventory will be published as soon as the connection is up");
            return;
        }

        await _publishLock.WaitAsync(cancellationToken);
        try
        {
            var fleet = _fleet.CurrentValue;
            var count = 0;

            foreach (var device in fleet.Devices)
            {
                var deviceInventory = inventory.Devices.FirstOrDefault(d => d.DeviceId == device.Id);
                var info = deviceInventory?.Info;

                count += await PublishAsync(DiscoveryPayloads.ConnectivityConfig(_options, device, info), cancellationToken);

                foreach (var app in fleet.AppsFor(device))
                {
                    count += await PublishAsync(DiscoveryPayloads.UpdateConfig(_options, device, info, app), cancellationToken);

                    // Without a fresh inventory the last retained state stays in place.
                    if (deviceInventory?.Apps.FirstOrDefault(a => a.AppId == app.Id) is { } entry)
                    {
                        count += await PublishAsync(DiscoveryPayloads.UpdateState(_options, device.Id, entry), cancellationToken);
                    }
                }

                if (deviceInventory is not null)
                {
                    var online = deviceInventory.Connection == ConnectionState.Online;
                    _availability[device.Id] = online;
                    count += await PublishAsync(AvailabilityMessage(device.Id, online), cancellationToken);
                }
            }

            _logger.LogInformation("Published {Count} messages to Home Assistant", count);
        }
        finally
        {
            _publishLock.Release();
        }
    }

    /// <summary>Publishes the availability of a device if it changed (or always with <paramref name="force"/>).</summary>
    public async Task PublishAvailabilityAsync(string deviceId, bool online, CancellationToken cancellationToken, bool force = false)
    {
        var changed = !_availability.TryGetValue(deviceId, out var previous) || previous != online;
        _availability[deviceId] = online;

        if (changed || force)
        {
            await PublishAsync(AvailabilityMessage(deviceId, online), cancellationToken);
        }
    }

    private async Task RepublishAsync(CancellationToken cancellationToken)
    {
        if (_lastInventory is { } inventory)
        {
            await PublishInventoryAsync(inventory, cancellationToken);
        }

        foreach (var (deviceId, online) in _availability)
        {
            await PublishAvailabilityAsync(deviceId, online, cancellationToken, force: true);
        }
    }

    private MqttMessage AvailabilityMessage(string deviceId, bool online) =>
        new(_options.AvailabilityTopic(deviceId), online ? "online" : "offline");

    private async Task<int> PublishAsync(MqttMessage message, CancellationToken cancellationToken) =>
        await _mqtt.PublishAsync(message.Topic, message.Payload, retain: true, cancellationToken) ? 1 : 0;
}
