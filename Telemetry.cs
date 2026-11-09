using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Droidwart.Worker;

/// <summary>Custom traces and metrics. The name matches Extensions.TelemetrySourceName in ServiceDefaults.</summary>
public static class Telemetry
{
    public const string SourceName = "Droidwart";

    public static readonly ActivitySource ActivitySource = new(SourceName);

    private static readonly Meter Meter = new(SourceName);

    public static readonly Counter<long> InventoryRuns =
        Meter.CreateCounter<long>("droidwart.inventory.runs", description: "Number of inventory runs");

    public static readonly Counter<long> DeviceInventories =
        Meter.CreateCounter<long>("droidwart.inventory.devices", description: "Inventory results per device and connection state");
}
