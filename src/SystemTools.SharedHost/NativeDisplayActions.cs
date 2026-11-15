using Avalonia.Controls;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using SystemTools.Contracts;
using SystemTools.Settings;

namespace SystemTools;

public static class NativeDisplayConfirmation
{
    public static async Task ApplyAsync(IDisplayConfiguration displays, DisplayLayout layout, CancellationToken token)
    {
        await using var change = await displays.ApplyAsync(layout, token);
        var choice = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var keep = new Button { Content = "保留显示设置" }; var revert = new Button { Content = "立即恢复" };
            var dialog = new Window { Title = "确认显示设置", Width = 440, Height = 190, CanResize = false,
                Content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 12,
                    Children = { new TextBlock { Text = "请在 15 秒内确认。超时或关闭将自动恢复。", TextWrapping = Avalonia.Media.TextWrapping.Wrap }, keep, revert } } };
            keep.Click += (_, _) => choice.TrySetResult(true); revert.Click += (_, _) => choice.TrySetResult(false);
            dialog.Closed += (_, _) => choice.TrySetResult(false);
            dialog.Show(); dialog.Activate(); return dialog;
        });
        try
        {
            var completed = await Task.WhenAny(choice.Task, change.Completion).WaitAsync(token);
            if (completed == change.Completion) { await change.Completion; return; }
            if (await choice.Task) await change.ConfirmAsync(token);
        }
        finally { await Dispatcher.UIThread.InvokeAsync(window.Close); }
    }
}

public abstract class NativeDisplayAction(NativeDesktopServices native, FeatureCatalog catalog, DisplayLayout layout) : ActionBase<ShortcutKeyNotificationSettings>
{
    protected override async Task OnInvoke()
    {
        catalog.EnsureExecutable(ActionItem.Id);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(InterruptCancellationToken, native.Stopping);
        await NativeDisplayConfirmation.ApplyAsync(native.Advanced.Displays!, layout, stop.Token);
    }
}
[ActionInfo("SystemTools.CloneDisplay", "复制屏幕", "\uE714", false)]
public sealed class NativeMirrorDisplayAction(NativeDesktopServices native, FeatureCatalog catalog) : NativeDisplayAction(native, catalog, DisplayLayout.Mirror);
[ActionInfo("SystemTools.ExtendDisplay", "扩展屏幕", "\uE714", false)]
public sealed class NativeExtendDisplayAction(NativeDesktopServices native, FeatureCatalog catalog) : NativeDisplayAction(native, catalog, DisplayLayout.Extend);
[ActionInfo("SystemTools.InternalDisplay", "仅电脑屏幕", "\uE714", false)]
public sealed class NativeInternalDisplayAction(NativeDesktopServices native, FeatureCatalog catalog) : NativeDisplayAction(native, catalog, DisplayLayout.InternalOnly);
[ActionInfo("SystemTools.ExternalDisplay", "仅第二屏幕", "\uE714", false)]
public sealed class NativeExternalDisplayAction(NativeDesktopServices native, FeatureCatalog catalog) : NativeDisplayAction(native, catalog, DisplayLayout.ExternalOnly);
