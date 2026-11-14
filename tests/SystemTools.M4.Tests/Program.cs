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
