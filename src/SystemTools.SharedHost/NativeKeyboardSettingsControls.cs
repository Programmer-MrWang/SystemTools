using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using ClassIsland.Core.Abstractions.Controls;
using SystemTools.Common.Automation;
using SystemTools.Settings;

namespace SystemTools;

public sealed class NativeKeyboardSettingsControl : ActionSettingsControlBase<KeyboardInputSettings>
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        var text = new TextBox { AcceptsReturn = true, MinHeight = 80, PlaceholderText = "输入 Unicode 文本；按键操作会在文本之后执行" };
        text.Bind(TextBox.TextProperty, new Binding(nameof(Settings.Text)) { Source = Settings, Mode = BindingMode.TwoWay });
        var capture = new TextBox { PlaceholderText = "聚焦这里后按键，记录逻辑键与间隔", IsReadOnly = true };
        var clock = Stopwatch.StartNew();
        capture.KeyDown += (_, args) =>
        {
            args.Handled = true;
            Settings.Actions.Add(new KeyboardAction { LogicalKey = NativeKeyNames.FromAvalonia(args.Key), Type = KeyboardAction.ActionType.Press,
                Interval = Settings.Actions.Count == 0 ? 0 : Math.Min(3600000, clock.ElapsedMilliseconds) });
            clock.Restart(); capture.Text = string.Join(" → ", Settings.Actions.Select(x => x.LogicalKey.Length == 0 ? x.KeyName : x.LogicalKey));
        };
        var clear = new Button { Content = "清除录制按键" };
        clear.Click += (_, _) => { Settings.Actions.Clear(); Settings.Keys.Clear(); capture.Text = ""; };
        Content = new StackPanel { Spacing = 10, Children = { new TextBlock { Text = "文本与逻辑键分别保存；旧 Windows 键值会保留并按明确映射读取。", TextWrapping = Avalonia.Media.TextWrapping.Wrap }, text, capture, clear } };
    }
}

public sealed class NativeCombinationSettingsControl : ActionSettingsControlBase<KeyCombinationSettings>
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        var capture = new TextBox { PlaceholderText = "聚焦后按组合键", IsReadOnly = true };
        capture.KeyDown += (_, args) =>
        {
            args.Handled = true;
            var keys = new List<string>();
            if (args.KeyModifiers.HasFlag(KeyModifiers.Control)) keys.Add("Control");
            if (args.KeyModifiers.HasFlag(KeyModifiers.Alt)) keys.Add("Alt");
            if (args.KeyModifiers.HasFlag(KeyModifiers.Shift)) keys.Add("Shift");
            if (args.KeyModifiers.HasFlag(KeyModifiers.Meta)) keys.Add("Meta");
            keys.Add(NativeKeyNames.FromAvalonia(args.Key));
            Settings.Keys = keys.Distinct().Select(key => new KeyCombinationKey { LogicalKey = key, KeyName = key }).ToList();
            capture.Text = string.Join(" + ", keys.Distinct());
        };
        Content = capture;
    }
}

internal static class NativeKeyNames
{
    internal static string FromAvalonia(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
        Key.Back => "BackSpace",
        _ => PortableKeyboard.Normalize(key.ToString())
    };
}
