using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using SystemTools.Contracts;
using SystemTools.Settings;

namespace SystemTools;

[ActionInfo("SystemTools.WindowOperation", "窗口操作", "\uF4B3", false)]
public sealed class NativeWindowOperationAction(IWindowOperations windows, NativeDesktopServices native, FeatureCatalog catalog) : ActionBase<WindowOperationSettings>
{
    protected override async Task OnInvoke()
    {
        catalog.EnsureExecutable(ActionItem.Id);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(InterruptCancellationToken, native.Stopping);
        await windows.ExecuteForegroundAsync(Settings.Operation, stop.Token);
        NativeActionFeedback.Notify(Settings.NotifyOnExecute, Settings.Operation == "关闭窗口" ? "已请求关闭目标窗口" : "窗口状态已更新");
    }
}
