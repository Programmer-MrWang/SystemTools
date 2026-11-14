using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using SystemTools.Common.Automation;
using SystemTools.Contracts;
using SystemTools.Settings;

namespace SystemTools;

[ActionInfo("SystemTools.SimulateKeyboard", "模拟键盘", "\uEA0F", false)]
public sealed class NativeKeyboardAction(IKeyboardInput keyboard, NativeDesktopServices native, FeatureCatalog catalog) : ActionBase<KeyboardInputSettings>
{
    protected override async Task OnInvoke()
    {
        catalog.EnsureExecutable(ActionItem.Id);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(InterruptCancellationToken, native.Stopping);
        var sequence = Settings.Actions.Select(action => (
            string.IsNullOrWhiteSpace(action.LogicalKey) ? PortableKeyboard.FromWindowsKey(action.KeyCode) : action.LogicalKey,
            (int)action.Type, action.Interval)).ToList();
        if (sequence.Count == 0)
            foreach (var legacy in Settings.Keys)
            {
                if (!uint.TryParse(legacy.Split(':', 2)[0], out var code)) throw new ArgumentException("旧按键数据无效，请重新录制。");
                sequence.Add((PortableKeyboard.FromWindowsKey(code), 0, 100));
            }
        await PortableKeyboard.ExecuteAsync(keyboard, sequence, Settings.Text, stop.Token);
        NativeActionFeedback.Notify(Settings.NotifyOnExecute, "键盘输入已完成");
    }
}

[ActionInfo("SystemTools.SimulateKeyCombination", "模拟组合键", "\uEA15", false)]
public sealed class NativeCombinationAction(IKeyboardInput keyboard, NativeDesktopServices native, FeatureCatalog catalog) : ActionBase<KeyCombinationSettings>
{
    protected override async Task OnInvoke()
    {
        catalog.EnsureExecutable(ActionItem.Id);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(InterruptCancellationToken, native.Stopping);
        var sequence = Settings.Keys.Take(5).Select(key => (string.IsNullOrWhiteSpace(key.LogicalKey)
            ? key.KeyCode is { } code ? PortableKeyboard.FromWindowsKey(code) : throw new ArgumentException("请选择有效按键。")
            : key.LogicalKey, 1, 20L)).ToArray();
        if (sequence.Length == 0) throw new ArgumentException("请先选择组合键。");
        await PortableKeyboard.ExecuteAsync(keyboard, sequence, "", stop.Token);
        NativeActionFeedback.Notify(Settings.NotifyOnExecute, "组合键输入已完成");
    }
}

public abstract class NativeSingleKeyAction(IKeyboardInput keyboard, NativeDesktopServices native, FeatureCatalog catalog) : ActionBase<ShortcutKeyNotificationSettings>
{
    protected abstract string Key { get; }
    protected override async Task OnInvoke()
    {
        catalog.EnsureExecutable(ActionItem.Id);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(InterruptCancellationToken, native.Stopping);
        await PortableKeyboard.ExecuteAsync(keyboard, [(Key, 0, 0)], "", stop.Token);
        NativeActionFeedback.Notify(Settings.NotifyOnExecute, "按键输入已完成");
    }
}
[ActionInfo("SystemTools.EnterKey", "按下 Enter 键", "\uEA0F", false)]
public sealed class NativeEnterAction(IKeyboardInput keyboard, NativeDesktopServices native, FeatureCatalog catalog) : NativeSingleKeyAction(keyboard, native, catalog) { protected override string Key => "Return"; }
[ActionInfo("SystemTools.EscKey", "按下 Esc 键", "\uEA0F", false)]
public sealed class NativeEscapeAction(IKeyboardInput keyboard, NativeDesktopServices native, FeatureCatalog catalog) : NativeSingleKeyAction(keyboard, native, catalog) { protected override string Key => "Escape"; }
[ActionInfo("SystemTools.F11Key", "按下 F11 键", "\uEA0F", false)]
public sealed class NativeF11Action(IKeyboardInput keyboard, NativeDesktopServices native, FeatureCatalog catalog) : NativeSingleKeyAction(keyboard, native, catalog) { protected override string Key => "F11"; }
