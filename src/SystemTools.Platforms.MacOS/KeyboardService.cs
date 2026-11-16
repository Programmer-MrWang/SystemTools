using SystemTools.Contracts;
using SystemTools.Common.Automation;

namespace SystemTools.Platforms.MacOS;

public sealed class KeyboardService : IKeyboardInput, IPermissionRequest, ISystemShortcutInput
{
    public Capability Probe() => NativeBridge.Probe("input.probe");
    public Capability RequestAccess() => NativeBridge.Probe("input.request");
    public Capability ProbeShortcut() => NativeBridge.Probe("input.shortcut.probe");
    public Task SendShortcutAsync(IReadOnlyList<string> keys, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var shortcut = MacSystemShortcut.Validate(keys);
        return Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            NativeBridge.Require(new { operation = "input.shortcut", keys = shortcut });
            token.ThrowIfCancellationRequested();
        }, token);
    }
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
