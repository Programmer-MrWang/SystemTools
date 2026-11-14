using System.Globalization;
using System.Text.Json;
using SystemTools.Common.Processes;
using SystemTools.Contracts;

namespace SystemTools.Platforms.Linux;

public sealed class SystemVolumeService : ISystemVolume
{
    private static string? Executable => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
        .Select(path => Path.Combine(path, "pactl")).FirstOrDefault(File.Exists);

    public Capability Probe()
    {
        if (!OperatingSystem.IsLinux()) return new(CapabilityState.Unsupported, "此实现需要 Linux。");
        if (Executable is null) return new(CapabilityState.MissingDependency, "请安装 PulseAudio 客户端工具，并确认音频服务已启动。");
        try { ReadAsync(default).GetAwaiter().GetResult(); return new(CapabilityState.Available); }
        catch (Exception error) when (error is IOException or TimeoutException or NativeFeatureException)
        { return new(CapabilityState.MissingDevice, "无法连接系统音频服务或没有输出设备。"); }
    }

    private static async Task<string> Run(CancellationToken token, params string[] arguments)
    {
        var executable = Executable ?? throw new NativeFeatureException(CapabilityState.MissingDependency, "缺少 pactl，请安装系统音频客户端工具。");
        var result = await CommandRunner.RunAsync(executable, arguments, token, TimeSpan.FromSeconds(3));
        if (result.ExitCode != 0) throw new NativeFeatureException(CapabilityState.MissingDevice, "系统音频服务或输出设备不可用。");
        return result.StandardOutput.Trim();
    }

    public async Task<SystemVolume> ReadAsync(CancellationToken token)
    {
        var sink = await Run(token, "get-default-sink");
        return await ReadSink(sink, token);
    }

    private static async Task<SystemVolume> ReadSink(string sink, CancellationToken token)
    {
        using var json = JsonDocument.Parse(await Run(token, "--format=json", "list", "sinks"));
        foreach (var item in json.RootElement.EnumerateArray())
        {
            if (item.GetProperty("name").GetString() != sink) continue;
            var channels = item.GetProperty("volume").EnumerateObject().Select(x => x.Value.GetProperty("value").GetDouble() / 65536 * 100).ToArray();
            if (channels.Length != 0) return new(sink, channels.Average());
        }
        throw new NativeFeatureException(CapabilityState.MissingDevice, "默认输出设备已断开。");
    }

    public async Task<SystemVolume> SetAsync(double percent, CancellationToken token)
    {
        if (!double.IsFinite(percent) || percent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(percent));
        var before = await ReadAsync(token);
        await Run(token, "set-sink-volume", before.DeviceId, percent.ToString("0.####", CultureInfo.InvariantCulture) + "%");
        return await ReadSink(before.DeviceId, token);
    }
}
