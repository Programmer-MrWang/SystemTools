using SystemTools.Common.Layout;
using SystemTools.Contracts;

namespace SystemTools.Platforms.MacOS;

public sealed class DisplayConfigurationService : IDisplayConfiguration, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IDisplayChange? _active;
    private bool _disposed;
    public Capability Probe() => NativeBridge.Probe("display.probe");
    public Capability ProbeLayout(DisplayLayout layout) => layout is DisplayLayout.InternalOnly or DisplayLayout.ExternalOnly
        ? new(CapabilityState.Unsupported, "Mac 公共显示 API 不提供可靠的单屏关闭接口，请在系统显示设置中操作。") : Probe();
    public async Task<IReadOnlyList<DesktopDisplay>> ListAsync(CancellationToken token)
    {
        var reply = await Task.Run(() => NativeBridge.Require(new { operation = "display.read" }), token);
        return reply.GetProperty("displays").EnumerateArray().Select(row => new DesktopDisplay(row.GetProperty("id").GetString()!, row.GetProperty("name").GetString()!,
            row.GetProperty("builtIn").GetBoolean(), new(row.GetProperty("x").GetDouble(), row.GetProperty("y").GetDouble(), row.GetProperty("width").GetDouble(), row.GetProperty("height").GetDouble()))).ToArray();
    }
    public async Task<IDisplayChange> ApplyAsync(DisplayLayout layout, CancellationToken token)
    {
        var state = ProbeLayout(layout); if (!state.CanExecute) throw new NativeFeatureException(state.State, state.Reason);
        if (!await _gate.WaitAsync(0, token)) throw new InvalidOperationException("另一个显示设置仍等待确认。");
        var leased = false;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var reply = await Task.Run(() => NativeBridge.Require(new { operation = "display.apply", layout = layout.ToString() }), token);
            var original = reply.GetProperty("original").GetString()!;
            var expected = reply.GetProperty("snapshot").GetString()!;
            async Task Restore() => await Task.Run(() => NativeBridge.Require(new { operation = "display.restore", original, expected }));
            if (token.IsCancellationRequested) { await Restore(); token.ThrowIfCancellationRequested(); }
            _active = new TimedDisplayChange(async () =>
            {
                var current = await Task.Run(() => NativeBridge.Require(new { operation = "display.read" }));
                if (current.GetProperty("snapshot").GetString() != expected) throw new InvalidOperationException("显示器状态已变化，无法确认旧配置。");
            }, Restore, () => _gate.Release());
            leased = true; return _active;
        }
        finally { if (!leased) _gate.Release(); }
    }
    public void Dispose() { _disposed = true; _active?.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
}
