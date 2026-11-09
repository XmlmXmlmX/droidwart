using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Droidwart.Worker;

/// <summary>Eigene Traces und Metriken. Der Name entspricht Extensions.TelemetrySourceName in ServiceDefaults.</summary>
public static class Telemetry
{
    public const string SourceName = "Droidwart";

    public static readonly ActivitySource ActivitySource = new(SourceName);

    private static readonly Meter Meter = new(SourceName);

    public static readonly Counter<long> InventoryRuns =
        Meter.CreateCounter<long>("droidwart.inventory.runs", description: "Anzahl der Inventar-Durchläufe");

    public static readonly Counter<long> DeviceInventories =
        Meter.CreateCounter<long>("droidwart.inventory.devices", description: "Inventar-Ergebnisse je Gerät und Verbindungsstatus");
}
