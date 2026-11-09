namespace Droidwart.Worker.Configuration;

public sealed class ScheduleOptions
{
    public const string Section = "Schedule";

    /// <summary>Abstand zwischen zwei Inventar-Läufen.</summary>
    public TimeSpan InventoryInterval { get; set; } = TimeSpan.FromHours(6);
}
