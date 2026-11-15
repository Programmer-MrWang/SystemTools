using SystemTools.Common.Media;
using SystemTools.Contracts;

namespace SystemTools.Platforms.MacOS;

public sealed class CameraService : ICameraCapture, IAsyncPermissionRequest, IAsyncCapabilityProbe, IDisposable
{
    private static readonly DeviceReservation Reservations = new();
    private readonly CompanionClient _client;
    private readonly IDisposable _ownership;
    public CameraService() : this(new CompanionClient()) { }
    internal CameraService(CompanionClient client)
    {
        _client = client;
        _ownership = client.Retain();
    }
    public Capability Probe() => _client.Probe("camera");
    public Task<Capability> ProbeAsync(CancellationToken token) => _client.ProbeAsync("camera", token);
    public Task<Capability> RequestAccessAsync(CancellationToken token) => _client.RequestAccessAsync("camera", token);
    public async Task<IReadOnlyList<CaptureDevice>> ListDevicesAsync(CancellationToken token)
    {
        var result = await _client.RequireAsync("camera.devices", null, token).ConfigureAwait(false);
        return result.GetProperty("devices").EnumerateArray()
            .Select(device => new CaptureDevice(device.GetProperty("id").GetString()!, device.GetProperty("name").GetString()!)).ToArray();
    }
    public async Task CapturePngAsync(string deviceId, string destination, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var lease = Reservations.Acquire("camera:" + deviceId, token);
        var id = Guid.NewGuid().ToString("D");
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            await _client.RequireAsync("camera.prepare", new { id }, token).ConfigureAwait(false);
            using var cancel = token.Register(() => _ = _client.EndSessionAsync("camera.cancel", id));
            var result = await _client.RequireAsync("camera.capture", new { id, device = deviceId }, token,
                TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            var image = result.GetProperty("png").GetString()!;
            if (image.Length > 22 * 1024 * 1024) throw new IOException("摄像头图像过大。");
            await File.WriteAllBytesAsync(temporary, Convert.FromBase64String(image), token).ConfigureAwait(false);
            MediaFiles.RequirePng(temporary);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            await _client.EndSessionAsync("camera.release", id).ConfigureAwait(false);
        }
    }
    public void Dispose() => _ownership.Dispose();
}
