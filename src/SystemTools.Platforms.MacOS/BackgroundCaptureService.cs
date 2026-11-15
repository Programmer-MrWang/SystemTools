using SystemTools.Contracts;

namespace SystemTools.Platforms.MacOS;

public sealed class BackgroundCaptureService : IBackgroundCapture, IPermissionRequest, IWindowRegionProvider
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public Capability Probe() => NativeBridge.Probe("background.probe");
    public Capability RequestAccess() => NativeBridge.Probe("screen.request");
    public Task<ScreenRegion> WindowRegionAsync(nint handle, ScreenRegion localRegion, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); localRegion.Validate();
        var value = NativeBridge.Require(new { operation = "background.region", handle = (long)handle,
            x = localRegion.X, y = localRegion.Y, width = localRegion.Width, height = localRegion.Height });
        return Task.FromResult(new ScreenRegion(value.GetProperty("x").GetDouble(), value.GetProperty("y").GetDouble(), localRegion.Width, localRegion.Height));
    }
    public async Task<BackgroundFrame> CaptureAsync(ScreenRegion region, CancellationToken token)
    {
        region.Validate();
        await _gate.WaitAsync(token);
        try
        {
            var reply = await Task.Run(() => NativeBridge.Require(new { operation = "background.capture", x = region.X, y = region.Y, width = region.Width, height = region.Height }), token);
            token.ThrowIfCancellationRequested();
            if (reply.GetProperty("excludedProcessId").GetInt32() != Environment.ProcessId)
                throw new IOException("背景采集未确认排除本进程，已拒绝使用该画面。");
            var data = reply.GetProperty("png").GetString()!;
            if (data.Length > 48_000_000) throw new IOException("背景帧过大，已停止处理。");
            return new(Convert.FromBase64String(data), region, reply.GetProperty("width").GetInt32(), reply.GetProperty("height").GetInt32(), true);
        }
        finally { _gate.Release(); }
    }
}
