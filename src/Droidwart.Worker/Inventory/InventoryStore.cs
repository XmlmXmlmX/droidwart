namespace Droidwart.Worker.Inventory;

/// <summary>Holds the latest inventory, shared by the MQTT publisher and the install service.</summary>
public sealed class InventoryStore
{
    private readonly Lock _lock = new();
    private FleetInventory? _current;

    public FleetInventory? Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    public void Set(FleetInventory inventory)
    {
        lock (_lock)
        {
            _current = inventory;
        }
    }

    public AppInventoryEntry? Find(string deviceId, string appId) =>
        Current?.Devices.FirstOrDefault(d => d.DeviceId == deviceId)?.Apps.FirstOrDefault(a => a.AppId == appId);

    /// <summary>Replaces a single app entry, e.g. right after an installation.</summary>
    public void Replace(string deviceId, AppInventoryEntry entry)
    {
        lock (_lock)
        {
            if (_current is null)
            {
                return;
            }

            _current = _current with
            {
                Devices = _current.Devices
                    .Select(d => d.DeviceId != deviceId
                        ? d
                        : d with { Apps = d.Apps.Select(a => a.AppId == entry.AppId ? entry : a).ToList() })
                    .ToList(),
            };
        }
    }
}
