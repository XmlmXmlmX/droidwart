using Droidwart.Worker.Configuration;
using Droidwart.Worker.Inventory;
using Microsoft.Extensions.Options;

namespace Droidwart.Worker.Services;

/// <summary>
/// Phase 1: reads the inventory at startup and then every Schedule:InventoryInterval.
/// With --RunOnce=true it performs a single run and stops the application.
/// </summary>
public sealed class InventoryWorker(
    InventoryCollector collector,
    InventoryReporter reporter,
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

        using var timer = new PeriodicTimer(schedule.Value.InventoryInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
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
            reporter.Log(inventory);
            await reporter.SaveAsync(inventory, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Inventory run failed");
        }
    }
}
