using SystemTools.Common.Processes;
using SystemTools.Contracts;

namespace SystemTools.Platforms.Linux;

public sealed class WindowOperationsService : IWindowOperations
{
    private static string? Find(string tool) => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
        .Select(path => Path.Combine(path, tool)).FirstOrDefault(File.Exists);
    private static async Task<string> Run(string tool, CancellationToken token, params string[] arguments)
    {
        var result = await CommandRunner.RunAsync(Find(tool) ?? throw new NativeFeatureException(CapabilityState.MissingDependency, "请安装 wmctrl、xdotool 与 xprop。"), arguments, token, TimeSpan.FromSeconds(5));
        if (result.ExitCode != 0) throw new NativeFeatureException(CapabilityState.MissingDevice, "目标窗口已关闭或窗口管理器拒绝操作。");
        return result.StandardOutput.Trim();
    }
    public Capability Probe()
    {
        try
        {
            using var connection = new X11Connection();
            if (new[] { "wmctrl", "xdotool", "xprop" }.Any(tool => Find(tool) is null))
                return new(CapabilityState.MissingDependency, "请安装 wmctrl、xdotool 与 xprop。");
            var supported = Run("xprop", default, "-root", "_NET_SUPPORTED").GetAwaiter().GetResult();
            return supported.Contains("_NET_WM_STATE", StringComparison.Ordinal) ? new(CapabilityState.Available)
                : new(CapabilityState.Unsupported, "当前窗口管理器没有提供 EWMH 窗口操作。");
        }
        catch (Exception error) when (error is IOException or TimeoutException or NativeFeatureException or DllNotFoundException)
        { return new(CapabilityState.MissingDependency, "无法连接 X11 窗口管理器。"); }
    }
    public async Task ExecuteForegroundAsync(string operation, CancellationToken token)
    {
        if (operation is not ("最大化" or "最小化" or "向下还原" or "关闭窗口")) throw new ArgumentException("不支持的窗口操作。");
        var target = await Run("xdotool", token, "getactivewindow");
        if (!ulong.TryParse(target, out var handle) || handle == 0) throw new InvalidOperationException("没有可操作的活动窗口。");
        var id = "0x" + handle.ToString("x");
        switch (operation)
        {
            case "最大化":
                await Run("wmctrl", token, "-ir", id, "-b", "add,maximized_vert,maximized_horz");
                await Verify("xprop", ["-id", id, "_NET_WM_STATE"], value => value.Contains("_NET_WM_STATE_MAXIMIZED_VERT") && value.Contains("_NET_WM_STATE_MAXIMIZED_HORZ"), token); break;
            case "最小化":
                await Run("xdotool", token, "windowminimize", target);
                await Verify("xprop", ["-id", id, "WM_STATE"], value => value.Contains("Iconic", StringComparison.OrdinalIgnoreCase), token); break;
            case "向下还原":
                await Run("wmctrl", token, "-ir", id, "-b", "remove,maximized_vert,maximized_horz");
                await Run("xdotool", token, "windowmap", target);
                await Verify("xprop", ["-id", id, "_NET_WM_STATE"], value => !value.Contains("_NET_WM_STATE_MAXIMIZED_"), token); break;
            case "关闭窗口":
                await Run("wmctrl", token, "-ic", id);
                // Applications may legitimately retain a window while asking to save a document.
                // This operation requests close; it never kills the owning process.
                break;
        }
    }
    private static async Task Verify(string tool, string[] args, Func<string, bool> expected, CancellationToken token)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (expected(await Run(tool, token, args))) return;
            await Task.Delay(50, token);
        }
        throw new InvalidOperationException("窗口管理器未确认操作结果，请检查目标窗口。");
    }
    public Capability ProbeBottommost()
    {
        var basic = Probe(); if (!basic.CanExecute) return basic;
        return Run("xprop", default, "-root", "_NET_SUPPORTED").GetAwaiter().GetResult().Contains("_NET_WM_STATE_BELOW")
            ? new(CapabilityState.Available) : new(CapabilityState.Unsupported, "当前窗口管理器不支持置底。");
    }
    public async Task SetBottommostAsync(nint handle, bool enabled, CancellationToken token)
    {
        if (handle == 0) throw new ArgumentException("窗口尚未创建。");
        var capability = ProbeBottommost();
        if (!capability.CanExecute) throw new NativeFeatureException(capability.State, capability.Reason);
        var id = "0x" + handle.ToString("x");
        await Run("wmctrl", token, "-ir", id, "-b", enabled ? "add,below" : "remove,below");
        await Verify("xprop", ["-id", id, "_NET_WM_STATE"], value => value.Contains("_NET_WM_STATE_BELOW") == enabled, token);
    }
}
