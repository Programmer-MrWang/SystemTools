namespace SystemTools.Common.Automation;

public static class MacSystemShortcut
{
    public static IReadOnlyList<string> Modifiers { get; } = Array.AsReadOnly(new[] { "Control", "Alt", "Shift", "Meta", "Fn" });
    public static IReadOnlyList<string> MainKeys { get; } = Array.AsReadOnly(
        Enumerable.Range(1, 12).Select(value => "F" + value)
            .Concat(Enumerable.Range('A', 26).Select(value => ((char)value).ToString()))
            .Concat(Enumerable.Range(0, 10).Select(value => value.ToString()))
            .Concat(new[] { "Space", "Tab", "Return", "Escape", "BackSpace", "Delete", "Left", "Right", "Up", "Down" }).ToArray());

    public static string[] Validate(IReadOnlyList<string>? keys)
    {
        if (keys is null || keys.Count == 0)
            throw new ArgumentException("请先在“系统功能与权限”中配置与 macOS“显示桌面”一致的快捷键。");
        if (keys.Count > Modifiers.Count + 1 || keys.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("显示桌面快捷键必须包含一个普通按键和可选修饰键。");
        var normalized = keys.Select(PortableKeyboard.Normalize).ToArray();
        if (normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length)
            throw new ArgumentException("显示桌面快捷键不能包含重复按键。");
        var main = normalized.Where(key => !Modifiers.Contains(key)).ToArray();
        if (main.Length != 1 || !MainKeys.Contains(main[0]))
            throw new ArgumentException("显示桌面快捷键必须包含一个支持的普通按键，请重新配置。");
        return Modifiers.Where(normalized.Contains).Append(main[0]).ToArray();
    }
}
