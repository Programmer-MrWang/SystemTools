using SystemTools.Contracts;

namespace SystemTools.Platforms.MacOS;

public sealed class WindowOperationsService : IWindowOperations, IPermissionRequest, IDisposable
{
    public Capability Probe() => NativeBridge.Probe("input.probe");
    public Capability RequestAccess() => NativeBridge.Probe("input.request");
    public Task ExecuteForegroundAsync(string operation, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        NativeBridge.Require(new { operation = "window.execute", action = operation });
    }, token);
    public Capability ProbeBottommost() => new(CapabilityState.Unsupported, "macOS 暂不提供与 Windows 置底等价的浮窗层级。原有层级配置已保留。");
    public Task SetBottommostAsync(nint handle, bool enabled, CancellationToken token) => enabled
        ? Task.FromException(new NativeFeatureException(CapabilityState.Unsupported, ProbeBottommost().Reason))
        : Task.CompletedTask;
    public void Dispose() => NativeBridge.Require(new { operation = "window.clear" });
}
