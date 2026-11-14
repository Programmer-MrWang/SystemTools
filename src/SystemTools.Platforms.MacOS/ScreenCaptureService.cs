using SystemTools.Contracts;

namespace SystemTools.Platforms.MacOS;

public sealed class ScreenCaptureService : IScreenCapture, IPermissionRequest
{
    public Capability Probe() => NativeBridge.Probe("screen.probe");
    public Capability RequestAccess() => NativeBridge.Probe("screen.request");
    public async Task CapturePngAsync(string destination, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await Task.Run(() => NativeBridge.Require(new { operation = "screen.capture", path = destination }), token);
        token.ThrowIfCancellationRequested();
    }
}
