using SystemTools.Common.Media;
using SystemTools.Contracts;

namespace SystemTools.Platforms.MacOS;

public sealed class CameraService : ICameraCapture, IAsyncPermissionRequest
{
    private static readonly DeviceReservation Reservations = new();
    public Capability Probe() => NativeBridge.Probe("camera.probe");
    public async Task<Capability> RequestAccessAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = await Task.Run(() => NativeBridge.Call(new { operation = "camera.request" }), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return NativeBridge.Status(result);
    }
    public Task<IReadOnlyList<CaptureDevice>> ListDevicesAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = NativeBridge.Require(new { operation = "camera.devices" });
        return Task.FromResult<IReadOnlyList<CaptureDevice>>(result.GetProperty("devices").EnumerateArray()
            .Select(device => new CaptureDevice(device.GetProperty("id").GetString()!, device.GetProperty("name").GetString()!)).ToArray());
    }
    public async Task CapturePngAsync(string deviceId, string destination, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var lease = Reservations.Acquire("camera:" + deviceId, token);
        var id = Guid.NewGuid().ToString("N");
        NativeBridge.Require(new { operation = "camera.prepare", id });
        try
        {
            using var cancel = token.Register(() => NativeBridge.Call(new { operation = "camera.cancel", id }));
            try
            {
                await Task.Run(() => NativeBridge.Require(new { operation = "camera.capture", id, device = deviceId, path = destination })).ConfigureAwait(false);
            }
            catch when (token.IsCancellationRequested) { token.ThrowIfCancellationRequested(); throw; }
            token.ThrowIfCancellationRequested();
            MediaFiles.RequirePng(destination);
        }
        finally { NativeBridge.Call(new { operation = "camera.release", id }); }
    }
}
