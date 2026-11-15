using SystemTools.Contracts;

namespace SystemTools.Common.Media;

public sealed class DeviceReservation
{
    private readonly HashSet<string> _owned = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    public IDisposable Acquire(string identity, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        token.ThrowIfCancellationRequested();
        lock (_sync)
        {
            token.ThrowIfCancellationRequested();
            if (!_owned.Add(identity))
                throw new NativeFeatureException(CapabilityState.MissingDevice, "设备正在被另一个操作使用，请先结束该操作。");
        }
        return new Lease(this, identity);
    }

    private sealed class Lease(DeviceReservation owner, string identity) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (owner._sync) owner._owned.Remove(identity);
        }
    }
}
