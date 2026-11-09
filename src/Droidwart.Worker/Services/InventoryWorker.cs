using Droidwart.Worker.Configuration;
using Droidwart.Worker.Inventory;
using Droidwart.Worker.Mqtt;
using Microsoft.Extensions.Options;

namespace Droidwart.Worker.Services;

/// <summary>
/// Reads the inventory at startup, then every Schedule:InventoryInterval or whenever a run is requested
/// (e.g. a device came back online), and publishes the result to Home Assistant.
/// With --RunOnce=true it performs a single run and stops the application.
/// </summary>
public sealed class InventoryWorker(
    InventoryCollector collector,
    InventoryReporter reporter,
    HomeAssistantPublisher publisher,
    InventoryStore store,
    InventoryTrigger trigger,
    IOptions<ScheduleOptions> schedule,
    IConfiguration configuration,
    IHostApplicationLifetime lifetime,
    ILogger<InventoryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunInventoryAsync(stoppingToken);

        if (configuration.GetValue<bool>("RunOnce"))
        {
            lifetime.StopApplication();
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var requested = await trigger.WaitAsync(schedule.Value.InventoryInterval, stoppingToken);
            if (requested)
            {
                logger.LogInformation("Inventory run requested");
            }

            await RunInventoryAsync(stoppingToken);
        }
    }

    private async Task RunInventoryAsync(CancellationToken stoppingToken)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("inventory run");
        Telemetry.InventoryRuns.Add(1);

        try
        {
            logger.LogInformation("Reading inventory…");
            var inventory = await collector.CollectAsync(stoppingToken);
            store.Set(inventory);
            reporter.Log(inventory);
            await reporter.SaveAsync(inventory, stoppingToken);
            await publisher.PublishInventoryAsync(inventory, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Inventory run failed");
        }
    }
}
