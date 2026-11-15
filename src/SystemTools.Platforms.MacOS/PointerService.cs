using SystemTools.Contracts;
namespace SystemTools.Platforms.MacOS;

public sealed class PointerService : IPointerInput
{
    public Capability Probe() => NativeBridge.Probe("input.probe");
    public string CaptureTarget() => NativeBridge.Require(new { operation = "input.target" }).GetProperty("target").GetString()!;
    public Task SendAsync(string operation, int x, int y, int value, string target, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        NativeBridge.Require(new { operation = "pointer.event", kind = operation, x, y, value, target });
    }, token);
}
