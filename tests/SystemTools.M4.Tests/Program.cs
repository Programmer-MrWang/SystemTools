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
