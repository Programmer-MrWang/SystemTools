using SystemTools.Common.Processes;
using SystemTools.Contracts;
namespace SystemTools.Platforms.Linux;

public sealed class DesktopSessionService : IDesktopSession
{
    private static string? Find(string name) => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
        .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => Path.Combine(x, name)).FirstOrDefault(File.Exists);
    public Capability Probe(string operation)
    {
        if (!OperatingSystem.IsLinux()) return new(CapabilityState.Unsupported, "需要 Linux 桌面会话。");
        try
        {
            PlatformSelection.Select(PlatformEnvironment.Current());
            var tool = operation switch { "desktop" => "wmctrl", "lock" => "loginctl", "shutdown" or "restart" or "sleep" => "systemctl", _ => "gsettings" };
            if (Find(tool) is null) return new(CapabilityState.MissingDependency, "请安装并配置 " + tool + "。");
            if (operation is "shutdown" or "restart" or "sleep")
            {
                if (Find("busctl") is null) return new(CapabilityState.MissingDependency, "需要 systemd-logind 和 busctl。");
                var method = operation switch { "shutdown" => "CanPowerOff", "restart" => "CanReboot", _ => "CanSuspend" };
                var reply = CommandRunner.RunAsync(Find("busctl")!, ["--system", "call", "org.freedesktop.login1", "/org/freedesktop/login1", "org.freedesktop.login1.Manager", method],
                    timeout: TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                return reply.ExitCode == 0 && (reply.StandardOutput.Contains("\"yes\"") || reply.StandardOutput.Contains("\"challenge\""))
                    ? new(CapabilityState.Available, "由 logind/系统授权决定是否执行。") : new(CapabilityState.PermissionRequired, "系统未允许此电源操作。");
            }
            if (operation.StartsWith("theme.", StringComparison.Ordinal) || operation.StartsWith("accent.", StringComparison.Ordinal))
            {
                if (!(Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "").Contains("GNOME", StringComparison.OrdinalIgnoreCase))
                    return new(CapabilityState.Unsupported, "系统外观当前仅接入 GNOME 的公开 gsettings schema；其他桌面保留原配置。");
                var key = operation.StartsWith("theme.") ? "color-scheme" : "accent-color";
                var reply = CommandRunner.RunAsync(Find("gsettings")!, ["range", "org.gnome.desktop.interface", key], timeout: TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                if (reply.ExitCode != 0) return new(CapabilityState.Unsupported, "当前 GNOME 没有公开的 " + key + " 设置。");
            }
            return new(CapabilityState.Available);
        }
        catch (Exception error) when (error is IOException or NativeFeatureException or TimeoutException or System.ComponentModel.Win32Exception)
        { return new(CapabilityState.MissingDependency, "桌面服务不可用，请检查当前登录会话。"); }
    }
    public async Task<string> ExecuteAsync(string operation, string? value, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var available = Probe(operation);
        if (!available.CanExecute) throw new NativeFeatureException(available.State, available.Reason);
        var key = operation.StartsWith("theme.", StringComparison.Ordinal) ? "color-scheme" : "accent-color";
        var (tool, args) = operation switch
        {
            "shutdown" => ("systemctl", new[] { "poweroff" }), "restart" => ("systemctl", new[] { "reboot" }), "sleep" => ("systemctl", new[] { "suspend" }),
            "lock" => ("loginctl", new[] { "lock-session" }), "desktop" => ("wmctrl", new[] { "-k", "on" }),
            "theme.read" or "accent.read" => ("gsettings", new[] { "get", "org.gnome.desktop.interface", key }),
            "theme.set" when value is "prefer-dark" or "default" or "prefer-light" => ("gsettings", new[] { "set", "org.gnome.desktop.interface", key, value }),
            "accent.set" when value is "blue" or "teal" or "green" or "yellow" or "orange" or "red" or "pink" or "purple" or "slate" => ("gsettings", new[] { "set", "org.gnome.desktop.interface", key, value }),
            _ => throw new ArgumentException("系统操作或设置值无效。")
        };
        var result = await CommandRunner.RunAsync(Find(tool)!, args, token, TimeSpan.FromSeconds(15));
        if (result.ExitCode != 0) throw new NativeFeatureException(CapabilityState.PermissionRequired, "系统拒绝了操作，请检查桌面授权。");
        if (operation.EndsWith(".set", StringComparison.Ordinal))
        {
            var read = await ExecuteAsync(operation.Replace(".set", ".read"), null, token);
            if (read != value) throw new IOException("系统未确认请求的外观值。");
        }
        return result.StandardOutput.Trim().Trim('\'');
    }
}
