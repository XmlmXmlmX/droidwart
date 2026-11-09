namespace Droidwart.Worker.Services;

/// <summary>Lets other components request an immediate inventory run (e.g. when a device comes back online).</summary>
public sealed class InventoryTrigger
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Request()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // A run is already requested.
        }
    }

    /// <summary>Waits until a run is requested or the timeout elapses. Returns true if requested.</summary>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _signal.WaitAsync(timeout, cancellationToken);
}
