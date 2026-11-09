namespace Droidwart.Worker.Installs;

/// <summary>
/// Throttles progress updates (every 5 %) and sends them strictly in order, so a late progress message
/// can never overwrite the final state in Home Assistant.
/// </summary>
public sealed class ProgressReporter(Func<int, Task> publish)
{
    private const int Step = 5;

    private Task _chain = Task.CompletedTask;
    private int _last = -Step;

    public void Report(double percent)
    {
        var value = (int)Math.Clamp(Math.Round(percent), 0, 100);
        if (value < _last + Step && value != 100)
        {
            return;
        }

        if (value == _last)
        {
            return;
        }

        _last = value;
        _chain = _chain.ContinueWith(_ => publish(value), TaskScheduler.Default).Unwrap();
    }

    /// <summary>Waits until all queued progress messages have been sent.</summary>
    public Task FlushAsync() => _chain;
}
