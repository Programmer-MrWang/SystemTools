using System.Text.Json;
using SystemTools.Common.Processes;
using SystemTools.Contracts;

namespace SystemTools.Platforms.Linux;

public sealed class UsbDevicesService : IUsbDevices
{
    public Capability Probe()
    {
        if (!OperatingSystem.IsLinux() || !Directory.Exists("/sys/bus/usb/devices")) return new(CapabilityState.MissingDependency, "系统没有提供 USB 设备目录。");
        try { SnapshotAsync(default).GetAwaiter().GetResult(); return new(CapabilityState.Available); }
        catch (Exception error) when (error is IOException or TimeoutException or NativeFeatureException)
        { return new(CapabilityState.MissingDependency, "请检查 util-linux 与系统 USB 挂载信息。"); }
    }

    public async Task<IReadOnlyList<UsbDevice>> SnapshotAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var devices = new Dictionary<string, UsbDevice>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateDirectories("/sys/bus/usb/devices"))
        {
            if (!File.Exists(Path.Combine(path, "idVendor")) || Path.GetFileName(path).StartsWith("usb", StringComparison.Ordinal)) continue;
            var real = Directory.ResolveLinkTarget(path, true)?.FullName ?? path;
            devices[real] = new(real, false, []);
        }
        var executable = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(path => Path.Combine(path, "lsblk")).FirstOrDefault(File.Exists)
            ?? throw new NativeFeatureException(CapabilityState.MissingDependency, "请安装 util-linux 以查询 USB 挂载卷。");
        var result = await CommandRunner.RunAsync(executable, ["--json", "--paths", "--output", "NAME,TRAN,MOUNTPOINTS"], token, TimeSpan.FromSeconds(3));
        if (result.ExitCode != 0) throw new IOException("无法读取 USB 卷状态。");
        using var document = JsonDocument.Parse(result.StandardOutput);
        foreach (var disk in document.RootElement.GetProperty("blockdevices").EnumerateArray())
        {
            if (!disk.TryGetProperty("tran", out var transport) || transport.GetString() != "usb") continue;
            var name = Path.GetFileName(disk.GetProperty("name").GetString()!);
            var sys = Path.Combine("/sys/class/block", name);
            var resolved = Directory.ResolveLinkTarget(sys, true)?.FullName;
            var parent = devices.Keys.Where(id => resolved?.StartsWith(id + Path.DirectorySeparatorChar, StringComparison.Ordinal) == true).OrderByDescending(id => id.Length).FirstOrDefault();
            if (parent is null) continue;
            var mounts = new List<string>();
            Gather(disk, mounts);
            devices[parent] = new(parent, true, mounts.Distinct(StringComparer.Ordinal).ToArray());
        }
        return devices.Values.ToArray();
    }

    private static void Gather(JsonElement node, List<string> mounts)
    {
        if (node.TryGetProperty("mountpoints", out var values) && values.ValueKind == JsonValueKind.Array)
            foreach (var value in values.EnumerateArray())
                if (value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } path && Directory.Exists(path)) mounts.Add(path);
        if (node.TryGetProperty("children", out var children)) foreach (var child in children.EnumerateArray()) Gather(child, mounts);
    }
}
