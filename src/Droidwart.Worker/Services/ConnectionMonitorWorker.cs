using Droidwart.Worker.Adb;
using Droidwart.Worker.Configuration;
using Droidwart.Worker.Mqtt;
using Microsoft.Extensions.Options;

namespace Droidwart.Worker.Services;

/// <summary>
/// Checks the ADB connection of every device in a short interval, keeps the availability in Home Assistant
/// current and triggers an inventory run when a device comes back online.
/// </summary>
public sealed class ConnectionMonitorWorker(
    IAdbGateway adb,
    IOptionsMonitor<FleetOptions> fleet,
    IOptions<ScheduleOptions> schedule,
    HomeAssistantPublisher publisher,
    InventoryTrigger trigger,
    IConfiguration configuration,
    ILogger<ConnectionMonitorWorker> logger) : BackgroundService
{
    private readonly Dictionary<string, bool> _lastOnline = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (configuration.GetValue<bool>("RunOnce"))
        {
            return;
        }

        using var timer = new PeriodicTimer(schedule.Value.ConnectionCheckInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var device in fleet.CurrentValue.Devices)
            {
                await CheckAsync(device, stoppingToken);
            }
        }
    }

    private async Task CheckAsync(DeviceOptions device, CancellationToken stoppingToken)
    {
        bool online;
        try
        {
            online = (await adb.EnsureConnectedAsync(device, stoppingToken)).State == ConnectionState.Online;
        }
        catch (AdbServerUnavailableException ex)
        {
            logger.LogDebug(ex, "ADB server not reachable during connection check");
            online = false;
        }

        var known = _lastOnline.TryGetValue(device.Id, out var wasOnline);
        _lastOnline[device.Id] = online;

        await publisher.PublishAvailabilityAsync(device.Id, online, stoppingToken);

        if (known && online && !wasOnline)
        {
            logger.LogInformation("{Device} is back online, starting inventory", device.Id);
            trigger.Request();
        }
        else if (known && !online && wasOnline)
        {
            logger.LogWarning("{Device} went offline", device.Id);
        }
    }
}
