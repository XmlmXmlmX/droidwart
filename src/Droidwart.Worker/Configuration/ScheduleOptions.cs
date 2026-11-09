namespace Droidwart.Worker.Configuration;

public sealed class ScheduleOptions
{
    public const string Section = "Schedule";

    /// <summary>Interval between two inventory runs.</summary>
    public TimeSpan InventoryInterval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// Interval of the lightweight connection check. It keeps the availability in Home Assistant current
    /// and triggers an inventory run as soon as a device comes back online.
    /// </summary>
    public TimeSpan ConnectionCheckInterval { get; set; } = TimeSpan.FromMinutes(1);
}
