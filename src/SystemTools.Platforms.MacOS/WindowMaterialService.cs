using SystemTools.Contracts;

namespace SystemTools.Platforms.MacOS;

public sealed class WindowMaterialService : IWindowMaterial
{
    public Capability Probe() => NativeBridge.Probe("material.probe");
    public IDisposable Apply(nint view, DesktopMaterial material, Action<MaterialResult> applied)
    {
        var id = Guid.NewGuid().ToString("N");
        var reply = NativeBridge.Require(new { operation = "material.apply", handle = (long)view, material = material.ToString(), id });
        var result = new MaterialResult(Enum.Parse<DesktopMaterial>(reply.GetProperty("applied").GetString()!),
            reply.TryGetProperty("reason", out var reason) ? reason.GetString()! : "");
        try { applied(result); return new Lease(id); }
        catch { NativeBridge.Require(new { operation = "material.release", id }); throw; }
    }
    private sealed class Lease(string id) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            NativeBridge.Require(new { operation = "material.release", id });
        }
    }
}
