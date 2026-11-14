using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using SystemTools.Common.Automation;
using SystemTools.Contracts;
using SystemTools.Triggers;

namespace SystemTools;

[TriggerInfo("SystemTools.UsbDeviceTrigger", "USB设备插入时", "\uF3A3")]
public sealed class NativeUsbTrigger(IUsbDevices devices, NativeDesktopServices native, ILogger<NativeUsbTrigger> logger) : TriggerBase<UsbDeviceTriggerConfig>
{
    private CancellationTokenSource? _stop;
    public override void Loaded()
    {
        UnLoaded();
        _stop = CancellationTokenSource.CreateLinkedTokenSource(native.Stopping);
        _ = NativeUsbMonitor.RunAsync(devices, () => Settings.OnlyUsbStorage, _ => Trigger(), _stop, logger);
    }
    public override void UnLoaded()
    {
        try { Interlocked.Exchange(ref _stop, null)?.Cancel(); } catch (ObjectDisposedException) { }
    }
}

internal static class NativeUsbMonitor
{
    internal static async Task RunAsync(IUsbDevices devices, Func<bool> storageOnly, Action<UsbDevice> arrived,
        CancellationTokenSource stop, ILogger logger)
    {
        var token = stop.Token;
        var state = new UsbArrivalState();
        var previousMode = storageOnly();
        var failed = false;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            do
            {
                try
                {
                    var snapshot = await devices.SnapshotAsync(token);
                    token.ThrowIfCancellationRequested();
                    var mode = storageOnly();
                    if (mode != previousMode) { state = new(); previousMode = mode; }
                    foreach (var device in state.Sample(snapshot, mode))
                    {
                        token.ThrowIfCancellationRequested(); arrived(device);
                    }
                    failed = false;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception error) { if (!failed) logger.LogWarning(error, "USB 监测暂不可用，恢复后重试。"); failed = true; }
            } while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { stop.Dispose(); }
    }
}
