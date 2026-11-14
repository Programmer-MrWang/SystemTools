using SystemTools.Contracts;

namespace SystemTools.Common.Automation;

public static class PortableKeyboard
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IKeyboardInput, SemaphoreSlim> Gates = new();
    public static string FromWindowsKey(uint value) => value switch
    {
        >= 0x41 and <= 0x5a => ((char)value).ToString(),
        >= 0x30 and <= 0x39 => ((char)value).ToString(),
        >= 0x70 and <= 0x7b => "F" + (value - 0x70 + 1),
        0x0d => "Return", 0x1b => "Escape", 0x20 => "Space", 0x09 => "Tab", 0x08 => "BackSpace",
        0x25 => "Left", 0x26 => "Up", 0x27 => "Right", 0x28 => "Down", 0x2e => "Delete",
        0x10 or 0xa0 => "Shift", 0x11 or 0xa2 => "Control", 0x12 or 0xa4 => "Alt", 0x5b or 0x5c => "Meta",
        _ => throw new NativeFeatureException(CapabilityState.Unsupported, "此 Windows 按键没有明确的跨平台映射，请重新选择逻辑键。")
    };

    public static string Normalize(string key) => key.Trim() switch
    {
        "Enter" => "Return", "Esc" => "Escape", "Ctrl" or "LeftCtrl" or "RightCtrl" => "Control",
        "LeftShift" or "RightShift" => "Shift", "LeftAlt" or "RightAlt" => "Alt", "Command" or "Super" or "LWin" or "RWin" => "Meta",
        var value when value.Length == 1 => value.ToUpperInvariant(),
        var value when value.Length > 0 => value,
        _ => throw new ArgumentException("请选择按键。")
    };

    public static async Task ExecuteAsync(IKeyboardInput keyboard, IReadOnlyList<(string Key, int Kind, long Delay)> sequence, string text, CancellationToken token)
    {
        if (sequence.Count > 10000 || text.Length > 100000) throw new ArgumentException("输入内容过长，请分批执行。");
        var gate = Gates.GetValue(keyboard, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        try { await ExecuteCoreAsync(keyboard, sequence, text, token); }
        finally { gate.Release(); }
    }

    private static async Task ExecuteCoreAsync(IKeyboardInput keyboard, IReadOnlyList<(string Key, int Kind, long Delay)> sequence, string text, CancellationToken token)
    {
        var target = keyboard.CaptureTarget();
        var held = new List<string>();
        try
        {
            if (text.Length != 0) await keyboard.TextAsync(text, target, token);
            foreach (var item in sequence)
            {
                if (item.Delay is < 0 or > 3600000) throw new ArgumentException("按键间隔必须在 0–3600000 毫秒之间。");
                if (item.Kind is < 0 or > 2) throw new ArgumentException("按键操作类型无效。");
                await Task.Delay(TimeSpan.FromMilliseconds(item.Delay), token);
                var key = Normalize(item.Key);
                if (item.Kind == 2)
                {
                    await keyboard.KeyAsync(key, false, target, token); held.Remove(key);
                }
                else
                {
                    if (!held.Contains(key)) held.Add(key);
                    await keyboard.KeyAsync(key, true, target, token);
                    if (item.Kind == 0)
                    {
                        await Task.Delay(20, token);
                        await keyboard.KeyAsync(key, false, target, token); held.Remove(key);
                    }
                }
            }
        }
        finally
        {
            List<Exception> failures = [];
            foreach (var key in held.AsEnumerable().Reverse())
                try { await keyboard.KeyAsync(key, false, target, CancellationToken.None); }
                catch (Exception error) { failures.Add(error); }
            if (failures.Count != 0) throw new AggregateException("部分按键释放失败，请检查系统输入状态。", failures);
        }
    }
}
