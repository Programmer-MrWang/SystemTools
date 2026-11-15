using SystemTools.Contracts;

namespace SystemTools.Common.Layout;

public sealed class UnsupportedBackgroundCapture : IBackgroundCapture
{
    public Capability Probe() => new(CapabilityState.Unsupported, "当前 X11 后端没有经过验证的排除自身捕获实现，背景明暗和 OCR 避让暂不可用。");
    public Task<BackgroundFrame> CaptureAsync(ScreenRegion region, CancellationToken token) =>
        Task.FromException<BackgroundFrame>(new NativeFeatureException(Probe().State, Probe().Reason));
}
