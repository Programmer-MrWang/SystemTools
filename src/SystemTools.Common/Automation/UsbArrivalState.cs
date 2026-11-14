using SystemTools.Contracts;

namespace SystemTools.Common.Automation;

public sealed class UsbArrivalState
{
    private bool _initialized;
    private readonly HashSet<string> _emitted = new(StringComparer.Ordinal);
    public IReadOnlyList<UsbDevice> Sample(IReadOnlyList<UsbDevice> devices, bool storageOnly)
    {
        var present = devices.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        _emitted.IntersectWith(present);
        var result = new List<UsbDevice>();
        if (!_initialized)
        {
            _emitted.UnionWith(present); _initialized = true;
            return result;
        }
        foreach (var device in devices)
        {
            if (_emitted.Contains(device.Id)) continue;
            if (storageOnly && (!device.Storage || device.Mounts.Count == 0)) continue;
            _emitted.Add(device.Id); result.Add(device);
        }
        return result;
    }
}
