using SystemTools.Contracts;

namespace SystemTools.Platforms.MacOS;

public sealed class KeyboardService : IKeyboardInput, IPermissionRequest
{
    public Capability Probe() => NativeBridge.Probe("input.probe");
    public Capability RequestAccess() => NativeBridge.Probe("input.request");
    public string CaptureTarget() => NativeBridge.Require(new { operation = "input.target" }).GetProperty("target").GetString()!;
    public Task KeyAsync(string key, bool down, string target, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        NativeBridge.Require(new { operation = "input.key", key, down, target });
    }, token);
    public Task TextAsync(string text, string target, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested(); NativeBridge.Require(new { operation = "input.text", text, target });
    }, token);
}
