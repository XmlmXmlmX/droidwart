using System.Threading.Channels;

namespace Droidwart.Worker.Installs;

public enum AppCommand
{
    Install,
    Start,
    Stop,
    Restart,
}

public sealed record InstallRequest(string DeviceId, string AppId, string Source, AppCommand Command = AppCommand.Install);

/// <summary>
/// App commands from Home Assistant. A request for the same device and app is only accepted once
/// until it has been processed, serializing controls with installations.
/// </summary>
public sealed class InstallQueue
{
    private readonly Channel<InstallRequest> _channel = Channel.CreateUnbounded<InstallRequest>(
        new UnboundedChannelOptions { SingleReader = true });

    private readonly HashSet<(string DeviceId, string AppId)> _pending = [];
    private readonly Lock _lock = new();

    /// <summary>Returns false if the same installation is already queued or running.</summary>
    public bool TryEnqueue(InstallRequest request)
    {
        lock (_lock)
        {
            if (!_pending.Add((request.DeviceId, request.AppId)))
            {
                return false;
            }
        }

        return _channel.Writer.TryWrite(request);
    }

    public IAsyncEnumerable<InstallRequest> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public void Complete(InstallRequest request)
    {
        lock (_lock)
        {
            _pending.Remove((request.DeviceId, request.AppId));
        }
    }
}
