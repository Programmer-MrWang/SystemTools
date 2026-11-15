using SystemTools.Contracts;

namespace SystemTools.Platforms.MacOS;

public sealed class WindowMaterialService : IWindowMaterial, IDisposable
{
    private readonly HashSet<string> _leases = [];
    private bool _disposed;
    public Capability Probe() => NativeBridge.Probe("material.probe");
    public IDisposable Apply(nint view, DesktopMaterial material, Action<MaterialResult> applied)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var id = Guid.NewGuid().ToString("N");
        var reply = NativeBridge.Require(new { operation = "material.apply", handle = (long)view, material = material.ToString(), id });
        var result = new MaterialResult(Enum.Parse<DesktopMaterial>(reply.GetProperty("applied").GetString()!),
            reply.TryGetProperty("reason", out var reason) ? reason.GetString()! : "");
        try { applied(result); _leases.Add(id); return new Lease(this, id); }
        catch { NativeBridge.Require(new { operation = "material.release", id }); throw; }
    }
    private void Release(string id)
    {
        if (!_leases.Remove(id)) return;
        NativeBridge.Require(new { operation = "material.release", id });
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception> failures = [];
        foreach (var id in _leases.ToArray())
            try { Release(id); } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("部分 Mac 材质未能恢复。", failures);
    }
    private sealed class Lease(WindowMaterialService owner, string id) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            owner.Release(id);
        }
    }
}
