using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Services.Registry;
using ClassIsland.Shared;
using ClassIsland.Shared.Models.Automation;
using Microsoft.Extensions.DependencyInjection;
using SystemTools.Contracts;
using SystemTools.Settings;
using SystemTools.Shared;
using SystemTools.Triggers;

namespace SystemTools.M3.DesktopProbe;

public sealed partial class Plugin
{
    private readonly List<string> _pendingM4 = [];
    private NativeDesktopServices? _native;
    private NativeFloatingTrigger? _floating;
    private TextBox? _inputTarget;

    private async Task RunM4Async()
    {
        _native = _provider.GetRequiredService<NativeDesktopServices>();
        var catalog = _provider.GetRequiredService<FeatureCatalog>();
        Check(_native.Volume is not null && _native.Idle is not null && _native.ScreenCapture is not null &&
            _native.UsbDevices is not null && _native.Keyboard is not null && _native.Hotkeys is not null &&
            _native.Windows is not null && _native.Wallpaper is not null, "M4 八类原生服务在真实宿主注册");
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var page = _provider.GetRequiredKeyedService<SettingsPageBase>("SystemTools.Native");
            Check(page is NativePermissionSettingsPage, "系统权限页实际构造");
            _inputTarget = new TextBox { Width = 400, Height = 80, AcceptsReturn = true };
            var panel = new StackPanel { Spacing = 12, Children = { new TextBlock { Text = "M4 合成测试窗口", FontSize = 22 }, _inputTarget, page } };
            _window!.Content = new ScrollViewer { Content = panel };
            _window.Activate(); _inputTarget.Focus();
        });
        var beforeVolume = await _native.Volume!.ReadAsync(default);
        try
        {
            await InvokeM4Async("SystemTools.SetVolume", new SystemTools.Actions.SetVolumeSettings { VolumePercent = (float)Math.Max(0, beforeVolume.Percent - 1) });
            Check(Math.Abs((await _native.Volume.ReadAsync(default)).Percent - Math.Max(0, beforeVolume.Percent - 1)) < 2, "真实宿主音量行动设置并读回");
        }
        finally
        {
            if ((await _native.Volume.ReadAsync(default)).DeviceId == beforeVolume.DeviceId) await _native.Volume.SetAsync(beforeVolume.Percent, default);
        }
        Check(_native.Idle!.Read() >= TimeSpan.Zero, "真实宿主 Quartz 空闲查询");
        var usb = await _native.UsbDevices!.SnapshotAsync(default);
        Check(usb.Select(x => x.Id).Distinct().Count() == usb.Count, "真实宿主 USB 快照物理身份唯一");
        foreach (var id in new[] { "SystemTools.LongIdleTrigger", "SystemTools.UsbDeviceTrigger", "SystemTools.HotkeyTrigger", "SystemTools.FloatingWindowTrigger" })
            Check(IAutomationService.RegisteredTriggers.Any(x => x.Id == id), id + " 已登记真实触发器");
        var hotkey = _native.Hotkeys!;
        await Task.Run(() =>
        {
            using var registration = hotkey.Register("F12", 11, () => { });
            Check(true, "Carbon 热键实际注册");
            try { using var duplicate = hotkey.Register("F12", 11, () => { }); throw new Exception("重复热键被接受"); }
            catch (NativeFeatureException) { Check(true, "重复热键明确报告冲突"); }
        });
        await CheckFloatingM4Async();
        var screen = _native.ScreenCapture!.Probe();
        if (screen.CanExecute && catalog.Get("SystemTools.ScreenShot").CanExecute)
        {
            var directory = Path.Combine(_root, "m4-screen");
            await InvokeM4Async("SystemTools.ScreenShot", new ScreenShotSettings { SaveFolder = directory });
            var png = Directory.GetFiles(directory, "*.png").Single();
            using var imageStream = File.OpenRead(png);
            using var image = NativeRasterImage.Read(imageStream, 100_000_000);
            Check(image.Width > 0 && image.Height > 0, "ScreenCaptureKit 实际 PNG 可解码且非空");
        }
        else _pendingM4.Add("真实录屏授权后采集：" + screen.Reason);
        await CheckWallpaperM4Async();
        await CheckWindowsM4Async();
        var keyboard = _native.Keyboard!;
        if (keyboard.Probe().CanExecute)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { _window!.Activate(); _inputTarget!.Focus(); });
            await Task.Delay(200);
            try
            {
                var target = await Task.Run(keyboard.CaptureTarget);
                if (target.StartsWith(Environment.ProcessId + ":", StringComparison.Ordinal))
                {
                    await InvokeM4Async("SystemTools.SimulateKeyboard", new KeyboardInputSettings { Text = "M4中文✓" });
                    await Task.Delay(250);
                    Check(await Dispatcher.UIThread.InvokeAsync(() => _inputTarget!.Text == "M4中文✓"), "Unicode 输入进入明确合成窗口");
                }
                else _pendingM4.Add("Unicode 实机输入：合成窗口尚未获得焦点。");
            }
            catch (NativeFeatureException error) when (error.State is CapabilityState.MissingDevice or CapabilityState.PermissionRequired)
            { _pendingM4.Add("Unicode 实机输入：" + error.Message); }
        }
        else _pendingM4.Add("辅助功能授权后 Unicode 输入与外部窗口操作。");
        File.WriteAllText(Path.Combine(_root, "m4-capabilities.json"), JsonSerializer.Serialize(new
        {
            features = catalog.Snapshot(), pending = _pendingM4,
            note = "未执行的权限、硬件与其他系统场景不计为运行通过。"
        }, Options));
    }

    private async Task InvokeM4Async(string id, object settings)
    {
        var action = _provider.GetRequiredKeyedService<ActionBase>(id);
        var item = new ActionItem { Id = id, Settings = settings };
        var set = new ActionSet(); set.ActionItems.Add(item); set.SetStartRunning(true);
        try { await action.InvokeAsync(item, set, isRevertable: false); }
        finally { set.SetEndRunning(true); }
        Check(item.IsCompleted && item.Exception is null, id + " 真实行动生命周期通过");
    }

    private async Task CheckFloatingM4Async()
    {
        _floating = (NativeFloatingTrigger)_provider.GetRequiredKeyedService<TriggerBase>("SystemTools.FloatingWindowTrigger");
        var settings = new FloatingWindowTriggerConfig { ButtonName = "M4合成按钮", ButtonId = "m4-synthetic-button" };
        typeof(ClassIsland.Core.Abstractions.Automation.TriggerBase).GetProperty("SettingsInternal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!.SetValue(_floating, settings);
        await Dispatcher.UIThread.InvokeAsync(_floating.Loaded);
        await Task.Delay(200);
        var service = _provider.GetRequiredService<NativeFloatingWindowService>();
        await Dispatcher.UIThread.InvokeAsync(service.Refresh);
        var window = typeof(NativeFloatingWindowService).GetField("_window", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(service) as Window;
        Check(window is { IsVisible: true }, "原生共享浮窗实际显示合成按钮");
        var action = _provider.GetRequiredKeyedService<ActionBase>("SystemTools.SwitchFloatingWindowTheme");
        var original = GlobalConstants.MainConfig!.Data.FloatingWindowTheme;
        var first = new ActionItem { Id = "SystemTools.SwitchFloatingWindowTheme", Settings = new SwitchFloatingWindowThemeSettings { TargetTheme = 2 } };
        var second = new ActionItem { Id = first.Id, Settings = new SwitchFloatingWindowThemeSettings { TargetTheme = 1 } };
        var set = new ActionSet(); set.ActionItems.Add(first); set.ActionItems.Add(second); set.SetStartRunning(true);
        try
        {
            await action.InvokeAsync(first, set, isRevertable: true); await action.InvokeAsync(second, set, isRevertable: true);
            await action.RevertAsync(first, set);
            Check(GlobalConstants.MainConfig.Data.FloatingWindowTheme == 1, "浮窗较早持有者恢复不覆盖后续主题");
            await action.RevertAsync(second, set);
            Check(GlobalConstants.MainConfig.Data.FloatingWindowTheme == original, "浮窗最后持有者恢复原主题");
        }
        finally { set.SetEndRunning(true); GlobalConstants.MainConfig.Data.FloatingWindowTheme = original; }
    }

    private async Task CheckWallpaperM4Async()
    {
        var wallpaper = _native!.Wallpaper!;
        var original = await wallpaper.ReadAsync(default);
        var path = Path.Combine(_root, "m4-synthetic-wallpaper.png");
        NativeRasterImage.WriteSolid(path, 64, 64, 24, 48, 72);
        WallpaperState? applied = null;
        try
        {
            applied = await wallpaper.ApplyAsync(path, 3, default);
            Check(applied.ImagePath == path, "真实 Mac 壁纸写入后读回路径");
            var color = NativeDesktopAppearance.ReadColor(path);
            Check(color.R == 24 && color.G == 48 && color.B == 72, "真实合成壁纸取色");
        }
        finally { if (applied is not null) await wallpaper.RestoreAsync(applied, original, default); }
        var restored = await wallpaper.ReadAsync(default);
        Check(restored.Snapshot == original.Snapshot, "真实 Mac 壁纸按完整显示器快照恢复");
        var otherPath = Path.Combine(_root, "m4-synthetic-wallpaper-other.png");
        File.Copy(path, otherPath);
        WallpaperState? lastWritten = null;
        try
        {
            var first = await wallpaper.ApplyAsync(path, 3, default);
            lastWritten = first;
            var later = await wallpaper.ApplyAsync(otherPath, 4, default);
            lastWritten = later;
            try { await wallpaper.RestoreAsync(first, original, default); throw new Exception("较早壁纸恢复覆盖了随后更改"); }
            catch (NativeFeatureException error) when (error.State == CapabilityState.Unsupported)
            { Check((await wallpaper.ReadAsync(default)).Snapshot == later.Snapshot, "条件恢复保留随后由其他持有者更改的真实壁纸"); }
        }
        finally { if (lastWritten is not null) await wallpaper.RestoreAsync(lastWritten, original, default); }
        Check((await wallpaper.ReadAsync(default)).Snapshot == original.Snapshot, "条件恢复验收后还原原始壁纸");
    }

    private async Task CheckWindowsM4Async()
    {
        if (Environment.GetEnvironmentVariable("SystemTools_M4_WindowFixture") is { } fixture)
        { await CheckExternalWindowsM4Async(fixture); return; }
        if (!_native!.Windows!.Probe().CanExecute || !_native.Keyboard!.Probe().CanExecute)
        { _pendingM4.Add("辅助功能授权后合成窗口最大化、还原、最小化和关闭。"); return; }
        Window? target = null;
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                target = new Window { Title = "M4 窗口操作专用合成目标", Width = 600, Height = 320, Content = new TextBlock { Text = "仅用于 SystemTools 窗口验收" } };
                target.Show(); target.Activate();
            });
            await Task.Delay(250);
            var identity = await Task.Run(_native.Keyboard.CaptureTarget);
            if (!identity.StartsWith(Environment.ProcessId + ":", StringComparison.Ordinal) ||
                !await Dispatcher.UIThread.InvokeAsync(() => target!.IsActive))
            { _pendingM4.Add("窗口操作：合成目标尚未获得焦点。"); return; }
            var before = await Dispatcher.UIThread.InvokeAsync(() => (target!.Position, target.Bounds.Size));
            async Task Execute(string operation)
            {
                Check(await Dispatcher.UIThread.InvokeAsync(() => target!.IsActive) && await Task.Run(_native.Keyboard.CaptureTarget) == identity,
                    "窗口操作前确认同一合成目标：" + operation);
                await InvokeM4Async("SystemTools.WindowOperation", new WindowOperationSettings { Operation = operation });
                await Task.Delay(250);
            }
            await Execute("最大化");
            Check(await Dispatcher.UIThread.InvokeAsync(() => target!.Bounds.Width > before.Size.Width), "Accessibility 实际最大化合成窗口");
            await Execute("向下还原");
            Check(await Dispatcher.UIThread.InvokeAsync(() => Math.Abs(target!.Bounds.Width - before.Size.Width) <= 4 &&
                Math.Abs(target.Bounds.Height - before.Size.Height) <= 4 && Math.Abs(target.Position.X - before.Position.X) <= 4 && Math.Abs(target.Position.Y - before.Position.Y) <= 4),
                "Accessibility 按原窗口身份恢复尺寸与位置");
            await Execute("最小化");
            Check(await Dispatcher.UIThread.InvokeAsync(() => target!.WindowState == WindowState.Minimized), "Accessibility 实际最小化合成窗口");
            await Dispatcher.UIThread.InvokeAsync(() => { target!.WindowState = WindowState.Normal; target.Activate(); });
            await Task.Delay(250);
            await Execute("关闭窗口");
            Check(await Dispatcher.UIThread.InvokeAsync(() => !target!.IsVisible), "Accessibility 请求关闭专用合成窗口");
        }
        finally { await Dispatcher.UIThread.InvokeAsync(() => target?.Close()); }
    }

    private async Task CheckExternalWindowsM4Async(string fixture)
    {
        if (!_native!.Windows!.Probe().CanExecute || !_native.Keyboard!.Probe().CanExecute)
        { _pendingM4.Add("辅助功能授权后外部合成窗口操作。"); return; }
        var statePath = Path.Combine(_root, "external-window.json");
        var start = new System.Diagnostics.ProcessStartInfo(fixture) { UseShellExecute = false };
        start.ArgumentList.Add(statePath);
        using var process = System.Diagnostics.Process.Start(start)!;
        async Task<JsonElement> Wait(Func<JsonElement, bool> predicate)
        {
            for (var i = 0; i < 60; i++)
            {
                if (File.Exists(statePath))
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(statePath));
                    if (predicate(document.RootElement)) return document.RootElement.Clone();
                }
                await Task.Delay(50);
            }
            throw new TimeoutException("外部合成窗口未确认目标状态。");
        }
        try
        {
            var original = await Wait(item => item.GetProperty("pid").GetInt32() == process.Id);
            string? identity = null;
            for (var i = 0; i < 40; i++)
            {
                identity = await Task.Run(_native.Keyboard.CaptureTarget);
                if (identity.StartsWith(process.Id + ":", StringComparison.Ordinal)) break;
                await Task.Delay(50);
            }
            Check(identity!.StartsWith(process.Id + ":", StringComparison.Ordinal), "外部合成目标取得真实焦点与独立进程身份");
            async Task Execute(string operation)
            {
                Check(await Task.Run(_native.Keyboard.CaptureTarget) == identity, "外部操作前确认同一合成目标：" + operation);
                await InvokeM4Async("SystemTools.WindowOperation", new WindowOperationSettings { Operation = operation });
            }
            await Execute("最大化");
            await Wait(item => item.GetProperty("width").GetDouble() > original.GetProperty("width").GetDouble());
            Check(true, "外部 AppKit 合成窗口实际最大化");
            await Execute("向下还原");
            await Wait(item => new[] { "x", "y", "width", "height" }.All(key => Math.Abs(item.GetProperty(key).GetDouble() - original.GetProperty(key).GetDouble()) <= 4));
            Check(true, "外部 AppKit 合成窗口原位置及尺寸恢复");
            File.WriteAllText(statePath + ".command", "minimize-baseline");
            var baseline = await Wait(item => item.TryGetProperty("baselineMinimized", out _));
            var canMinimize = baseline.GetProperty("baselineMinimized").GetBoolean();
            File.WriteAllText(statePath + ".command", "activate");
            await Wait(item => !item.GetProperty("minimized").GetBoolean());
            await Task.Delay(200);
            if (canMinimize)
            {
                await Execute("最小化");
                await Wait(item => item.GetProperty("minimized").GetBoolean());
                Check(true, "外部 AppKit 合成窗口实际最小化");
                File.WriteAllText(statePath + ".command", "activate");
                await Wait(item => !item.GetProperty("minimized").GetBoolean());
                await Task.Delay(200);
            }
            else
            {
                try { await Execute("最小化"); throw new Exception("系统未最小化但插件报告成功"); }
                catch (NativeFeatureException error) when (error.State == CapabilityState.Unsupported)
                {
                    Check(true, "系统 AppKit 最小化基线被拒绝时插件明确失败");
                    _pendingM4.Add("最小化实际成功：本机会话直接 AppKit performMiniaturize 也未改变状态；插件失败反馈已验收。");
                }
            }
            File.WriteAllText(statePath + ".command", "activate");
            await Wait(item => !item.GetProperty("minimized").GetBoolean());
            await Task.Delay(250);
            await Execute("关闭窗口");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Check(process.ExitCode == 0, "外部合成窗口收到关闭请求后正常退出");
        }
        finally { if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); } }
    }
}
