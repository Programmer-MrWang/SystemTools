namespace SystemTools.Contracts;

public interface INativeDesktopBackend : IPlatformBackend
{
    NativeDesktopServices CreateServices();
}

public interface INativeCapability
{
    Capability Probe();
}

public sealed record SystemVolume(string DeviceId, double Percent);

public interface ISystemVolume : INativeCapability
{
    Task<SystemVolume> ReadAsync(CancellationToken token);
    Task<SystemVolume> SetAsync(double percent, CancellationToken token);
}

public interface IUserIdle : INativeCapability
{
    TimeSpan Read();
}

public sealed class NativeDesktopServices : IDisposable
{
    public ISystemVolume? Volume { get; init; }
    public IUserIdle? Idle { get; init; }
    private readonly CancellationTokenSource _stop = new();
    public CancellationToken Stopping => _stop.Token;
    private int _disposed;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        List<Exception> failures = [];
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var resource in new object?[] { Volume, Idle }.OfType<IDisposable>())
            if (seen.Add(resource))
                try { resource.Dispose(); } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("部分平台资源清理失败。", failures);
    }
}

public sealed class NativeFeatureException(CapabilityState state, string message) : InvalidOperationException(message)
{
    public CapabilityState State { get; } = state;
}
