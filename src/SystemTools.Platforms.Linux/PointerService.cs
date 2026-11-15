using SystemTools.Common.Processes;
using SystemTools.Contracts;
namespace SystemTools.Platforms.Linux;

public sealed class PointerService : IPointerInput
{
    private readonly KeyboardService _keyboard = new();
    public Capability Probe() => _keyboard.Probe();
    public string CaptureTarget() => _keyboard.CaptureTarget();
    public async Task SendAsync(string operation, int x, int y, int value, string target, CancellationToken token)
    {
        if (operation != "up" && CaptureTarget() != target) throw new InvalidOperationException("输入目标已改变，已停止鼠标操作。");
        string[] args = operation switch
        {
            "move" or "drag" => ["mousemove", "--sync", "--", x.ToString(System.Globalization.CultureInfo.InvariantCulture), y.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            "down" or "up" when value is >= 1 and <= 3 => [operation == "down" ? "mousedown" : "mouseup", value.ToString()],
            "scroll" when Math.Abs((long)value) <= 100 => ["click", "--repeat", Math.Abs(value).ToString(), "--delay", "10", value > 0 ? "4" : "5"],
            _ => throw new ArgumentException("鼠标操作无效。")
        };
        var result = await CommandRunner.RunAsync("xdotool", args, token, TimeSpan.FromSeconds(5));
        if (result.ExitCode != 0) throw new InvalidOperationException("X11 鼠标输入失败。");
    }
}
