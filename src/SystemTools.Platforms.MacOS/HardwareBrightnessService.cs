using System.Globalization;
using SystemTools.Common.Layout;
using SystemTools.Common.Media;
using SystemTools.Common.Processes;
using SystemTools.Contracts;

namespace SystemTools.Platforms.MacOS;

public sealed class HardwareBrightnessService : IHardwareBrightness
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static string? Ddc => MediaFiles.FindExecutable("m1ddc") ?? new[] { "/opt/homebrew/bin/m1ddc", "/usr/local/bin/m1ddc" }.FirstOrDefault(File.Exists);
    private static async Task<int> ReadDdc(string id, string command, CancellationToken token)
    {
        var result = await CommandRunner.RunAsync(Ddc ?? throw new IOException("外屏 DDC 需要安装 m1ddc。"), ["display", "id=" + id, command, "luminance"], token, TimeSpan.FromSeconds(5));
        if (result.ExitCode != 0 || !int.TryParse(result.StandardOutput.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new IOException("此外屏未返回有效 DDC 亮度，可能不支持该端口或命令。");
        return value;
    }
    public Capability Probe()
    {
        try { return Task.Run(() => ListAsync(default)).GetAwaiter().GetResult().Count > 0 ? new(CapabilityState.Available) : new(CapabilityState.Unsupported, "本机没有可读写的公共 IOKit 背光后端；外屏需安装 m1ddc 且显示器/端口支持 DDC，软件遮罩不作为背光替代。"); }
        catch (Exception error) { return new(CapabilityState.MissingDevice, error.Message); }
    }
    public async Task<IReadOnlyList<BrightnessDevice>> ListAsync(CancellationToken token)
    {
        var reply = await Task.Run(() => NativeBridge.Require(new { operation = "brightness.read" }), token);
        var devices = reply.GetProperty("devices").EnumerateArray().Select(row => new BrightnessDevice(row.GetProperty("id").GetString()!, "内置背光", BrightnessKind.BuiltInBacklight, row.GetProperty("percent").GetDouble())).ToList();
        if (Ddc is null) return devices;
        var displays = await new DisplayConfigurationService().ListAsync(token);
        foreach (var display in displays.Where(x => !x.BuiltIn))
        {
            try
            {
                var maximum = await ReadDdc(display.Id, "max", token);
                var value = await ReadDdc(display.Id, "get", token);
                devices.Add(new("ddc:" + display.Id, display.Name + "（DDC）", BrightnessKind.ExternalDdc, HardwareBrightnessPolicy.ToPercent(value, maximum)));
            }
            catch (Exception error) when (error is IOException or TimeoutException) { }
        }
        return devices;
    }
    public async Task<BrightnessDevice> SetAsync(string id, double percent, CancellationToken token)
    {
        HardwareBrightnessPolicy.Validate(percent);
        await _gate.WaitAsync(token);
        try
        {
            var device = (await ListAsync(token)).SingleOrDefault(x => x.Id == id) ?? throw new IOException("所选亮度设备已移除或不再可用。");
            if (device.Kind == BrightnessKind.BuiltInBacklight)
            {
                await Task.Run(() => NativeBridge.Require(new { operation = "brightness.set", id, percent }), token);
            }
            else
            {
                var number = id[4..]; var maximum = await ReadDdc(number, "max", token);
                var result = await CommandRunner.RunAsync(Ddc!, ["display", "id=" + number, "set", "luminance", HardwareBrightnessPolicy.ToRaw(percent, maximum).ToString(CultureInfo.InvariantCulture)], token, TimeSpan.FromSeconds(5));
                if (result.ExitCode != 0) throw new IOException("外屏拒绝 DDC 亮度调整。");
            }
            var current = (await ListAsync(token)).SingleOrDefault(x => x.Id == id) ?? throw new IOException("写入后设备不可读，请检查显示器。");
            if (Math.Abs(current.Percent - percent) > 3) throw new IOException("显示器未确认请求的硬件亮度。");
            return current;
        }
        finally { _gate.Release(); }
    }
}
