using SystemTools.Contracts;

var checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine($"PASS {++checks}: {name}"); }
var macVolume = new SystemTools.Platforms.MacOS.SystemVolumeService();
var linuxVolume = new SystemTools.Platforms.Linux.SystemVolumeService();
foreach (var volume in new ISystemVolume[] { macVolume, linuxVolume })
    foreach (var value in new[] { -1d, 101d, double.NaN, double.PositiveInfinity })
    {
        try { await volume.SetAsync(value, default); throw new Exception("Invalid volume accepted"); }
        catch (ArgumentOutOfRangeException) { Check(true, "拒绝非法音量 " + value); }
    }
if (OperatingSystem.IsMacOS())
{
    var probe = macVolume.Probe();
    Console.WriteLine($"Mac volume capability: {probe.State}: {probe.Reason}");
    if (probe.CanExecute)
    {
        var before = await macVolume.ReadAsync(default);
        Check(before.Percent is >= 0 and <= 100, "读取真实系统主音量");
        if (args.Contains("--volume-write"))
        {
            try
            {
                var value = Math.Max(0, before.Percent - 1);
                var after = await macVolume.SetAsync(value, default);
                Check(after.DeviceId == before.DeviceId && Math.Abs(after.Percent - value) < 2, "真实设置并读回系统音量");
            }
            finally
            {
                if ((await macVolume.ReadAsync(default)).DeviceId == before.DeviceId)
                    await macVolume.SetAsync(before.Percent, default);
            }
            Check(Math.Abs((await macVolume.ReadAsync(default)).Percent - before.Percent) < 2, "恢复原系统音量");
        }
    }
}
Console.WriteLine($"M4 checks: {checks}; OS={System.Runtime.InteropServices.RuntimeInformation.OSDescription}");

var state = new SystemTools.Common.Automation.IdleTriggerState();
Check(state.Sample(TimeSpan.FromSeconds(59), TimeSpan.FromMinutes(1)) == SystemTools.Common.Automation.IdleTransition.None, "空闲阈值前不触发");
Check(state.Sample(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1)) == SystemTools.Common.Automation.IdleTransition.Trigger, "空闲阈值只触发一次");
Check(state.Sample(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(1)) == SystemTools.Common.Automation.IdleTransition.None, "持续空闲不重复触发");
Check(state.Sample(TimeSpan.Zero, TimeSpan.FromMinutes(1)) == SystemTools.Common.Automation.IdleTransition.Revert, "重新操作后恢复");
Check(state.Sample(TimeSpan.Zero, TimeSpan.FromMinutes(1)) == SystemTools.Common.Automation.IdleTransition.None, "恢复不重复执行");
if (OperatingSystem.IsMacOS()) Check(new SystemTools.Platforms.MacOS.UserIdleService().Read() >= TimeSpan.Zero, "读取真实 Quartz 空闲时长");

if (OperatingSystem.IsMacOS())
{
    var screen = new SystemTools.Platforms.MacOS.ScreenCaptureService();
    var screenState = screen.Probe();
    Check(screenState.State is CapabilityState.Available or CapabilityState.PermissionRequired, "原生截图模块只读检测权限");
    Console.WriteLine($"Screenshot: {screenState.State}: {screenState.Reason}");
    using var stop = new CancellationTokenSource(); stop.Cancel();
    try { await screen.CapturePngAsync(Path.Combine(Path.GetTempPath(), "must-not-create.png"), stop.Token); throw new Exception("取消失败"); }
    catch (OperationCanceledException) { Check(true, "取消截图不进入系统采集"); }
}

var arrivals = new SystemTools.Common.Automation.UsbArrivalState();
Check(arrivals.Sample([new("existing", true, ["/existing"])], true).Count == 0, "启动不触发已有 USB");
Check(arrivals.Sample([new("existing", true, ["/existing"]), new("new", true, [])], true).Count == 0, "USB 等待卷就绪");
Check(arrivals.Sample([new("new", true, ["/one", "/two"])], true).Count == 1, "同一物理 USB 多分区只触发一次");
Check(arrivals.Sample([new("new", true, ["/one"])], true).Count == 0, "持续挂载不重复触发");
arrivals.Sample([], true);
Check(arrivals.Sample([new("new", true, ["/one"])], true).Count == 1, "拔出后重新插入可触发");
if (OperatingSystem.IsMacOS())
{
    var usb = new SystemTools.Platforms.MacOS.UsbDevicesService();
    Check(usb.Probe().CanExecute, "IOKit 与挂载卷只读查询可用");
    var snapshot = await usb.SnapshotAsync(default);
    Check(snapshot.Select(x => x.Id).Distinct().Count() == snapshot.Count, "真实 USB 快照无重复物理身份");
}

Check(SystemTools.Common.Automation.PortableKeyboard.FromWindowsKey(0x0d) == "Return" && SystemTools.Common.Automation.PortableKeyboard.FromWindowsKey(0x7a) == "F11", "Windows 按键按明确映射转换");
try { SystemTools.Common.Automation.PortableKeyboard.FromWindowsKey(0xff); throw new Exception("Unknown VK accepted"); }
catch (NativeFeatureException) { Check(true, "未知 Windows 按键拒绝猜测"); }
var fakeKeyboard = new FailingKeyboard();
try { await SystemTools.Common.Automation.PortableKeyboard.ExecuteAsync(fakeKeyboard, [("Control", 1, 0), ("A", 1, 0)], "", default); throw new Exception("Failure swallowed"); }
catch (IOException) { Check(fakeKeyboard.Released.SequenceEqual(new[] { "A", "Control" }), "输入失败后逆序释放全部已按下的键"); }
if (OperatingSystem.IsMacOS())
{
    var keyboard = new SystemTools.Platforms.MacOS.KeyboardService();
    Check(keyboard.Probe().State is CapabilityState.Available or CapabilityState.PermissionRequired, "输入权限只读检测");
    Check(new SystemTools.Platforms.MacOS.HotkeysService().Probe().CanExecute, "原生热键模块只读检测");
}
Console.WriteLine($"FINAL M4 checks: {checks}");

sealed class FailingKeyboard : IKeyboardInput
{
    public List<string> Released { get; } = [];
    public Capability Probe() => new(CapabilityState.Available);
    public string CaptureTarget() => "test-target";
    public Task TextAsync(string text, string target, CancellationToken token) => Task.CompletedTask;
    public Task KeyAsync(string key, bool down, string target, CancellationToken token)
    {
        if (down && key == "A") throw new IOException("synthetic failure");
        if (!down) Released.Add(key);
        return Task.CompletedTask;
    }
}
