namespace Droidwart.Worker.Configuration;

public sealed class ScheduleOptions
{
    public const string Section = "Schedule";

    /// <summary>Interval between two inventory runs.</summary>
    public TimeSpan InventoryInterval { get; set; } = TimeSpan.FromHours(6);
}
