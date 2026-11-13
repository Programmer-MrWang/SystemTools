namespace SystemTools.Common.Lifecycle;

/// <summary>Reverse-order, once-only cleanup; one failure never skips the remaining resources.</summary>
public sealed class LifetimeScope : IDisposable
{
    private readonly object _gate = new();
    private List<Action>? _cleanup = [];
    public void Own(Action cleanup)
    {
        lock (_gate)
        {
            if (_cleanup is null) throw new ObjectDisposedException(nameof(LifetimeScope));
            _cleanup.Add(cleanup);
        }
    }
    public IReadOnlyList<Exception> Close()
    {
        List<Action> cleanup;
        lock (_gate) { if (_cleanup is null) return []; cleanup = _cleanup; _cleanup = null; }
        var errors = new List<Exception>();
        foreach (var action in cleanup.AsEnumerable().Reverse())
            try { action(); } catch (Exception error) { errors.Add(error); }
        return errors;
    }
    public void Dispose()
    {
        var errors = Close();
        if (errors.Count > 0) throw new AggregateException("部分资源清理失败，其余资源已继续清理。", errors);
    }
}
