using SystemTools.Contracts;

namespace SystemTools.Platforms.MacOS;

public sealed class WindowMaterialService : IWindowMaterial, IDisposable
{
    private readonly Dictionary<string, Lease> _leases = [];
    private bool _disposed;
    public Capability Probe() => NativeBridge.Probe("material.probe");
    public IDisposable Apply(nint view, DesktopMaterial material, Action<MaterialResult> applied)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var id = Guid.NewGuid().ToString("N");
        var reply = NativeBridge.Require(new { operation = "material.apply", handle = (long)view, material = material.ToString(), id });
        var result = new MaterialResult(Enum.Parse<DesktopMaterial>(reply.GetProperty("applied").GetString()!),
            reply.TryGetProperty("reason", out var reason) ? reason.GetString()! : "");
        try { applied(result); var lease = new Lease(this, id, applied); _leases.Add(id, lease); return lease; }
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
        foreach (var lease in _leases.Values.ToArray())
            try { lease.Dispose(); } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("部分 Mac 材质未能恢复。", failures);
    }
    private sealed class Lease : IDisposable
    {
        private readonly WindowMaterialService _owner;
        private readonly string _id;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _poll;
        private int _disposed;
        internal Lease(WindowMaterialService owner, string id, Action<MaterialResult> applied)
        {
            _owner = owner; _id = id;
            _poll = Poll(applied);
        }
        private async Task Poll(Action<MaterialResult> applied)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            MaterialResult? previous = null;
            try
            {
                while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
                {
                    var reply = NativeBridge.Require(new { operation = "material.status", id = _id });
                    var result = new MaterialResult(Enum.Parse<DesktopMaterial>(reply.GetProperty("applied").GetString()!), reply.GetProperty("reason").GetString()!);
                    if (!_stop.IsCancellationRequested && result != previous) { previous = result; applied(result); }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (NativeFeatureException) when (_stop.IsCancellationRequested) { }
            catch (Exception) { if (!_stop.IsCancellationRequested) applied(new(DesktopMaterial.Solid, "材质状态暂不可读取，请重新打开窗口。")); }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _stop.Cancel(); _owner.Release(_id);
            _ = _poll.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
        }
    }
}
