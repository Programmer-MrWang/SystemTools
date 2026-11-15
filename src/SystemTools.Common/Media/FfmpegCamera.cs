using System.Text.RegularExpressions;
using SystemTools.Common.Processes;
using SystemTools.Contracts;

namespace SystemTools.Common.Media;

public sealed class FfmpegCamera(string runtime, Func<string?> executable) : ICameraCapture
{
    private static readonly DeviceReservation Reservations = new();

    public Capability Probe()
    {
        if (runtime is not ("win-x64" or "linux-x64"))
            return new(CapabilityState.Unsupported, "此采集后端不支持当前系统。");
        if (executable() is not { } path || !File.Exists(path))
            return new(CapabilityState.MissingDependency, "请在媒体设置中选择已安装的 FFmpeg。");
        if (runtime == "linux-x64" && !Directory.Exists("/sys/class/video4linux"))
            return new(CapabilityState.MissingDevice, "没有检测到视频设备。");
        return new(CapabilityState.Available);
    }

    public async Task<IReadOnlyList<CaptureDevice>> ListDevicesAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var capability = Probe();
        if (!capability.CanExecute) throw new NativeFeatureException(capability.State, capability.Reason);
        if (runtime == "linux-x64")
            return Directory.EnumerateDirectories("/sys/class/video4linux", "video*")
                .Select(path => new CaptureDevice("/dev/" + Path.GetFileName(path), File.ReadAllText(Path.Combine(path, "name")).Trim()))
                .Where(device => File.Exists(device.Id)).OrderBy(device => device.Id, StringComparer.Ordinal).ToArray();
        var result = await CommandRunner.RunAsync(executable()!,
            ["-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy"], token, TimeSpan.FromSeconds(10));
        // FFmpeg intentionally exits nonzero after listing DirectShow devices.
        return ParseDirectShowDevices(result.StandardError);
    }

    public static IReadOnlyList<CaptureDevice> ParseDirectShowDevices(string text) =>
        Regex.Matches(text, "\"([^\"\\r\\n]+)\" \\(video\\)")
            .Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal)
            .Select(name => new CaptureDevice(name, name)).ToArray();

    public async Task CapturePngAsync(string deviceId, string destination, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var devices = await ListDevicesAsync(token).ConfigureAwait(false);
        if (!devices.Any(device => device.Id == deviceId))
            throw new NativeFeatureException(CapabilityState.MissingDevice, "所选摄像头已不可用，请重新选择设备。");
        using var lease = Reservations.Acquire("camera:" + runtime + ":" + deviceId, token);
        var arguments = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-f", runtime == "win-x64" ? "dshow" : "video4linux2", "-i",
            runtime == "win-x64" ? "video=" + deviceId : deviceId,
            "-frames:v", "1", "-c:v", "png", "-f", "image2", "-y", destination };
        var result = await CommandRunner.RunAsync(executable()!, arguments, token, TimeSpan.FromSeconds(20)).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (result.ExitCode != 0)
            throw new NativeFeatureException(CapabilityState.MissingDevice, "摄像头采集失败，请检查访问权限、设备是否被占用或已断开。");
        MediaFiles.RequirePng(destination);
    }
}
