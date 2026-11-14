using SystemTools.Common.Processes;
using SystemTools.Contracts;

namespace SystemTools.Platforms.Linux;

public sealed class KeyboardService : IKeyboardInput
{
    private static string? Tool => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
        .Select(path => Path.Combine(path, "xdotool")).FirstOrDefault(File.Exists);
    private static async Task<string> Run(CancellationToken token, params string[] args)
    {
        var result = await CommandRunner.RunAsync(Tool ?? throw new NativeFeatureException(CapabilityState.MissingDependency, "请安装 xdotool 与 XTest 扩展。"), args, token, TimeSpan.FromSeconds(10));
        if (result.ExitCode != 0) throw new NativeFeatureException(CapabilityState.MissingDevice, "X11 输入目标不可用或输入操作失败。");
        return result.StandardOutput.Trim();
    }
    public Capability Probe()
    {
        try { using var connection = new X11Connection(); return Tool is null ? new(CapabilityState.MissingDependency, "请安装 xdotool 与 XTest 扩展。") : new(CapabilityState.Available); }
        catch (Exception error) when (error is DllNotFoundException or NativeFeatureException) { return new(CapabilityState.MissingDependency, "需要原生 X11 与 XTest 输入服务。"); }
    }
    public string CaptureTarget() => Run(default, "getactivewindow").GetAwaiter().GetResult();
    private async Task EnsureTarget(string target, CancellationToken token)
    {
        if (await Run(token, "getactivewindow") != target) throw new InvalidOperationException("输入目标已改变，已停止后续输入。");
    }
    public async Task KeyAsync(string key, bool down, string target, CancellationToken token)
    {
        if (down) await EnsureTarget(target, token);
        await Run(token, down ? "keydown" : "keyup", "--", Symbol(key));
    }
    public async Task TextAsync(string text, string target, CancellationToken token)
    {
        await EnsureTarget(target, token);
        await Run(token, "type", "--clearmodifiers", "--delay", "0", "--", text);
    }
    internal static string Symbol(string key) => key switch { "Control" => "Control_L", "Shift" => "Shift_L", "Alt" => "Alt_L", "Meta" => "Super_L", "Space" => "space", _ => key };
}
