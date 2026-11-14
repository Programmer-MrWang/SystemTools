using System.Text.Json;
using SystemTools.Contracts;

namespace SystemTools.Platforms.MacOS;

public sealed class UsbDevicesService : IUsbDevices
{
    public Capability Probe() => NativeBridge.Probe("usb.probe");
    public async Task<IReadOnlyList<UsbDevice>> SnapshotAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = await Task.Run(() => NativeBridge.Require(new { operation = "usb.snapshot" }), token);
        token.ThrowIfCancellationRequested();
        return result.GetProperty("devices").Deserialize<UsbDevice[]>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
}
