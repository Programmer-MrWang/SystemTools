using System.Globalization;
using System.Text.RegularExpressions;
using SystemTools.Common.Layout;
using SystemTools.Common.Media;
using SystemTools.Common.Processes;
using SystemTools.Contracts;

namespace SystemTools.Platforms.Linux;

public sealed class HardwareBrightnessService : IHardwareBrightness
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private const string BacklightRoot = "/sys/class/backlight";
    private static async Task<string> Ddc(CancellationToken token, params string[] arguments)
    {
        var result = await CommandRunner.RunAsync(MediaFiles.FindExecutable("ddcutil") ?? throw new IOException("请安装 ddcutil 并授予当前用户 I²C 权限。"), arguments, token, TimeSpan.FromSeconds(5));
        if (result.ExitCode != 0) throw new IOException("DDC 读取或写入失败，请检查显示器和 I²C 权限。");
        return result.StandardOutput;
    }
    public Capability Probe()
    {
        try { return ListAsync(default).GetAwaiter().GetResult().Count > 0 ? new(CapabilityState.Available) : new(CapabilityState.MissingDevice, "没有可写 sysfs 背光或可读取的 DDC 显示器，请检查设备、ddcutil 和权限。"); }
        catch (Exception error) { return new(CapabilityState.PermissionRequired, error.Message); }
    }
    public async Task<IReadOnlyList<BrightnessDevice>> ListAsync(CancellationToken token)
    {
        var devices = new List<BrightnessDevice>();
        if (Directory.Exists(BacklightRoot))
            foreach (var folder in Directory.EnumerateDirectories(BacklightRoot))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using var write = File.Open(Path.Combine(folder, "brightness"), FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                    var current = int.Parse(await File.ReadAllTextAsync(Path.Combine(folder, File.Exists(Path.Combine(folder, "actual_brightness")) ? "actual_brightness" : "brightness"), token), CultureInfo.InvariantCulture);
                    var maximum = int.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "max_brightness"), token), CultureInfo.InvariantCulture);
                    devices.Add(new("sysfs:" + Path.GetFileName(folder), Path.GetFileName(folder), BrightnessKind.BuiltInBacklight, HardwareBrightnessPolicy.ToPercent(current, maximum)));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException) { }
            }
        if (MediaFiles.FindExecutable("ddcutil") is null) return devices;
        string detected;
        try { detected = await Ddc(token, "detect", "--brief"); }
        catch (Exception error) when (devices.Count > 0 && error is IOException or TimeoutException) { return devices; }
        foreach (Match bus in Regex.Matches(detected, @"I2C bus:\s*/dev/i2c-(\d+)"))
        {
            var number = bus.Groups[1].Value;
            try
            {
                var values = await ReadDdc(number, token);
                devices.Add(new("ddc:" + number, "DDC /dev/i2c-" + number, BrightnessKind.ExternalDdc, HardwareBrightnessPolicy.ToPercent(values.Current, values.Maximum)));
            }
            catch (Exception error) when (error is IOException or TimeoutException) { }
        }
        return devices;
    }
    private static async Task<(int Current, int Maximum)> ReadDdc(string bus, CancellationToken token)
    {
        var text = await Ddc(token, "--bus", bus, "getvcp", "10", "--terse");
        var value = Regex.Match(text.Trim(), @"^VCP\s+10\s+C\s+(\d+)\s+(\d+)$");
        if (!value.Success) throw new IOException("显示器返回了不支持的 DDC 亮度格式。");
        return (int.Parse(value.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(value.Groups[2].Value, CultureInfo.InvariantCulture));
    }
    public async Task<BrightnessDevice> SetAsync(string id, double percent, CancellationToken token)
    {
        HardwareBrightnessPolicy.Validate(percent);
        await _gate.WaitAsync(token);
        try
        {
            var selected = (await ListAsync(token)).SingleOrDefault(x => x.Id == id) ?? throw new IOException("所选亮度设备已移除或权限不足。");
            if (selected.Kind == BrightnessKind.BuiltInBacklight)
            {
                var folder = Path.Combine(BacklightRoot, id[6..]);
                var maximum = int.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "max_brightness"), token), CultureInfo.InvariantCulture);
                await File.WriteAllTextAsync(Path.Combine(folder, "brightness"), HardwareBrightnessPolicy.ToRaw(percent, maximum).ToString(CultureInfo.InvariantCulture), token);
            }
            else
            {
                var values = await ReadDdc(id[4..], token);
                await Ddc(token, "--bus", id[4..], "setvcp", "10", HardwareBrightnessPolicy.ToRaw(percent, values.Maximum).ToString(CultureInfo.InvariantCulture));
            }
            var current = (await ListAsync(token)).SingleOrDefault(x => x.Id == id) ?? throw new IOException("亮度写入后设备未能读回。");
            if (Math.Abs(current.Percent - percent) > 3) throw new IOException("设备未确认请求的硬件亮度。");
            return current;
        }
        finally { _gate.Release(); }
    }
}
