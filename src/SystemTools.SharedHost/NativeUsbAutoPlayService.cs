using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Platforms.Abstraction;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SystemTools.Contracts;
using SystemTools.Settings;
using SystemTools.Shared;

namespace SystemTools;

public sealed class NativeUsbAutoPlayService(IUsbDevices devices, NativeDesktopServices native, ILogger<NativeUsbAutoPlayService> logger) : IHostedService
{
    private CancellationTokenSource? _stop;
    private Task? _task;
    public Task StartAsync(CancellationToken token)
    {
        _stop = CancellationTokenSource.CreateLinkedTokenSource(native.Stopping);
        _task = NativeUsbMonitor.RunAsync(devices, () => true, Arrived, _stop, logger);
        return Task.CompletedTask;
    }
    private void Arrived(UsbDevice device)
    {
        if (GlobalConstants.MainConfig?.Data.AutoOpenUsbDriveOnInsert != true || native.Stopping.IsCancellationRequested) return;
        var path = device.Mounts.FirstOrDefault(Directory.Exists);
        if (path is not null) _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                if (!native.Stopping.IsCancellationRequested && GlobalConstants.MainConfig?.Data.AutoOpenUsbDriveOnInsert == true && Directory.Exists(path))
                    await PlatformServices.LauncherService.LaunchPath(path);
            }
            catch (Exception error) { logger.LogWarning(error, "USB 卷打开失败。"); }
        });
    }
    public async Task StopAsync(CancellationToken token)
    {
        try { _stop?.Cancel(); } catch (ObjectDisposedException) { }
        if (_task is not null) await _task.WaitAsync(token);
    }
}

[ActionInfo("SystemTools.AutoOpenUsbDriveOnInsert", "自动播放", "\uEE81", false)]
public sealed class NativeUsbAutoPlayAction(FeatureCatalog catalog) : ActionBase<AutoOpenUsbDriveOnInsertActionSettings>
{
    protected override Task OnInvoke()
    {
        catalog.EnsureExecutable(ActionItem.Id);
        GlobalConstants.MainConfig!.Data.AutoOpenUsbDriveOnInsert = Settings.Enable;
        GlobalConstants.MainConfig.Save();
        NativeActionFeedback.Notify(Settings.NotifyOnExecute, Settings.Enable ? "已开启 USB 自动播放" : "已关闭 USB 自动播放");
        return Task.CompletedTask;
    }
}

public sealed class NativeUsbAutoPlaySettingsControl : ActionSettingsControlBase<AutoOpenUsbDriveOnInsertActionSettings>
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        var enabled = new CheckBox { Content = "自动打开新插入的 USB 存储卷" };
        enabled.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(Settings.Enable)) { Source = Settings, Mode = BindingMode.TwoWay });
        Content = enabled;
    }
}
