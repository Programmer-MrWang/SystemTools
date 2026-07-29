using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;
using SystemTools.Settings;
using SystemTools.Shared;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.ActionFlowExecutionConfirmation", "行动流执行确认", "\uE01D", false)]
public class ActionFlowExecutionConfirmationAction(
    IActionService actionService,
    ILogger<ActionFlowExecutionConfirmationAction> logger)
    : ActionBase<ActionFlowExecutionConfirmationSettings>
{
    private const string ContinueResult = "continue";
    private const string DelayResult = "delay";
    private const string DelayConfirmedResult = "delayConfirmed";
    private const string ConfirmDelayResult = "confirmDelay";
    private const string CancelDelayResult = "cancelDelay";
    private const string StopActionFlowResult = "stopActionFlow";
    private const string InterruptedResult = "interrupted";

    private FATaskDialog? _activeDialog;
    private FATaskDialog? _activeDelayDialog;
    private bool _isDelayDialogOpen;

    protected override async Task OnInvoke()
    {
        await base.OnInvoke();

        var outcome = await ShowConfirmationAsync();
        if (outcome.DelaySeconds is int delaySeconds)
        {
            logger.LogInformation("行动流“{ActionSetName}”将在 {DelaySeconds} 秒后继续执行。",
                ActionSet.Name, delaySeconds);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), InterruptCancellationToken);
            }
            catch (OperationCanceledException) when (InterruptCancellationToken.IsCancellationRequested)
            {
                // 行动流已被外部中断，无需继续等待。
            }

            return;
        }

        if (!Equals(outcome.Result, StopActionFlowResult))
        {
            return;
        }

        logger.LogInformation("用户停止了行动流“{ActionSetName}”。", ActionSet.Name);

        // InterruptActionSetAsync 会等待当前行动结束，因此这里只发起中断而不等待。
        _ = actionService.InterruptActionSetAsync(ActionSet);
    }

    protected override async Task OnInterrupted()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _activeDelayDialog?.Hide(InterruptedResult);
            _activeDialog?.Hide(InterruptedResult);
        });
        await base.OnInterrupted();
    }

    private Task<ConfirmationOutcome> ShowConfirmationAsync()
    {
        var resultSource = new TaskCompletionSource<ConfirmationOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (InterruptCancellationToken.IsCancellationRequested)
                {
                    resultSource.TrySetResult(new ConfirmationOutcome(InterruptedResult));
                    return;
                }

                var mainWindow = AppBase.Current.MainWindow
                                 ?? throw new InvalidOperationException("ClassIsland 主窗口尚未初始化");
                var promptName = string.IsNullOrWhiteSpace(Settings.PromptName)
                    ? "未命名自动化"
                    : Settings.PromptName.Trim();

                var dialog = new FATaskDialog
                {
                    XamlRoot = mainWindow,
                    Title = "SystemTools - 行动流执行确认",
                    Header = "行动流执行确认",
                    SubHeader = $"即将执行自动化“{promptName}”，是否执行？",
                    IconSource = new FAFontIconSource { Glyph = "\uE01D" }
                };
                dialog.Buttons.Add(new FATaskDialogButton("停止行动流", StopActionFlowResult));
                dialog.Buttons.Add(new FATaskDialogButton("延迟执行", DelayResult));
                dialog.Buttons.Add(new FATaskDialogButton("立即执行", ContinueResult)
                {
                    IsDefault = true
                });
                int? delaySeconds = null;
                dialog.Opened += (_, _) => RestoreDialogPosition(dialog, mainWindow);
                dialog.Closing += (sender, e) =>
                {
                    if (Equals(e.Result, DelayResult))
                    {
                        e.Cancel = true;
                        if (!_isDelayDialogOpen)
                        {
                            _ = HandleDelayRequestAsync(dialog, seconds => delaySeconds = seconds);
                        }

                        return;
                    }

                    var isAllowedToClose = Equals(e.Result, ContinueResult) ||
                                           Equals(e.Result, DelayConfirmedResult) ||
                                           Equals(e.Result, StopActionFlowResult) ||
                                           Equals(e.Result, InterruptedResult);
                    if (!isAllowedToClose)
                    {
                        e.Cancel = true;
                        return;
                    }

                    RememberDialogPosition(dialog);
                };

                _activeDialog = dialog;
                var result = await dialog.ShowAsync(showHosted: false);
                resultSource.TrySetResult(new ConfirmationOutcome(result, delaySeconds));
            }
            catch (Exception ex)
            {
                resultSource.TrySetException(ex);
            }
            finally
            {
                _activeDialog = null;
            }
        });

        return resultSource.Task;
    }

    private async Task HandleDelayRequestAsync(FATaskDialog parentDialog, Action<int> setDelaySeconds)
    {
        _isDelayDialogOpen = true;
        try
        {
            var delaySeconds = await ShowDelayDialogAsync(parentDialog);
            if (delaySeconds is not int seconds || InterruptCancellationToken.IsCancellationRequested)
            {
                return;
            }

            setDelaySeconds(seconds);
            parentDialog.Hide(DelayConfirmedResult);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "显示延迟执行设置窗口时发生错误。");
        }
        finally
        {
            _isDelayDialogOpen = false;
        }
    }

    private async Task<int?> ShowDelayDialogAsync(FATaskDialog parentDialog)
    {
        if (TopLevel.GetTopLevel(parentDialog) is not Window parentWindow)
        {
            throw new InvalidOperationException("无法获取行动流执行确认窗口");
        }

        var secondsInput = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 86400,
            Increment = 1,
            Value = 10,
            FormatString = "F0",
            Width = 160,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
        };
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock { Text = "延迟秒数" });
        content.Children.Add(secondsInput);

        var dialog = new FATaskDialog
        {
            XamlRoot = parentWindow,
            Title = "SystemTools - 延迟执行",
            Header = "延迟执行",
            SubHeader = "设置多少秒后继续执行该行动流。",
            Content = content,
            IconSource = new FAFontIconSource { Glyph = "\uE01D" }
        };
        dialog.Buttons.Add(new FATaskDialogButton("取消", CancelDelayResult));
        dialog.Buttons.Add(new FATaskDialogButton("确认", ConfirmDelayResult)
        {
            IsDefault = true
        });
        dialog.Opened += (_, _) =>
        {
            RestoreDelayDialogPosition(dialog, parentWindow);
            if (TopLevel.GetTopLevel(dialog) is Window window)
            {
                window.Topmost = true;
                window.Activate();
            }
        };
        dialog.Closing += (_, _) => RememberDelayDialogPosition(dialog);

        _activeDelayDialog = dialog;
        var previousParentTopmost = parentWindow.Topmost;
        parentWindow.Topmost = false;
        try
        {
            var result = await dialog.ShowAsync(showHosted: false);
            if (!Equals(result, ConfirmDelayResult))
            {
                return null;
            }

            return Math.Max(1, (int)(secondsInput.Value ?? 10));
        }
        finally
        {
            _activeDelayDialog = null;
            parentWindow.Topmost = previousParentTopmost;
            if (parentWindow.IsVisible)
            {
                parentWindow.Activate();
            }
        }
    }

    private static void RestoreDialogPosition(FATaskDialog dialog, Window owner)
    {
        if (TopLevel.GetTopLevel(dialog) is not Window window)
        {
            return;
        }

        var scaling = Math.Max(0.5, window.RenderScaling);
        var widthPx = (int)Math.Round(window.Bounds.Width * scaling);
        var heightPx = (int)Math.Round(window.Bounds.Height * scaling);
        if (widthPx <= 0 || heightPx <= 0)
        {
            return;
        }

        var config = GlobalConstants.MainConfig?.Data;
        if (config?.ActionFlowExecutionConfirmationPositionX is int savedX &&
            config.ActionFlowExecutionConfirmationPositionY is int savedY)
        {
            var savedPosition = new PixelPoint(savedX, savedY);
            var savedRect = new PixelRect(savedPosition, new PixelSize(widthPx, heightPx));
            if (owner.Screens.All.Any(screen => screen.WorkingArea.Intersects(savedRect)))
            {
                window.Position = savedPosition;
                return;
            }
        }

        CenterDialogWindow(window, owner, widthPx, heightPx);
    }

    private static void CenterDialogWindow(Window window, Window owner, int widthPx, int heightPx)
    {
        var screen = owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        window.Position = new PixelPoint(
            area.X + (area.Width - widthPx) / 2,
            area.Y + (area.Height - heightPx) / 2);
    }

    private static void CenterDialogOverOwner(FATaskDialog dialog, Window owner)
    {
        if (TopLevel.GetTopLevel(dialog) is not Window window)
        {
            return;
        }

        var scaling = Math.Max(0.5, window.RenderScaling);
        var ownerScaling = Math.Max(0.5, owner.RenderScaling);
        var widthPx = (int)Math.Round(window.Bounds.Width * scaling);
        var heightPx = (int)Math.Round(window.Bounds.Height * scaling);
        var ownerWidthPx = (int)Math.Round(owner.Bounds.Width * ownerScaling);
        var ownerHeightPx = (int)Math.Round(owner.Bounds.Height * ownerScaling);
        if (widthPx <= 0 || heightPx <= 0 || ownerWidthPx <= 0 || ownerHeightPx <= 0)
        {
            return;
        }

        var screen = owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var desiredX = owner.Position.X + (ownerWidthPx - widthPx) / 2;
        var desiredY = owner.Position.Y + (ownerHeightPx - heightPx) / 2;
        window.Position = new PixelPoint(
            Math.Clamp(desiredX, area.X, Math.Max(area.X, area.Right - widthPx)),
            Math.Clamp(desiredY, area.Y, Math.Max(area.Y, area.Bottom - heightPx)));
    }

    private static void RestoreDelayDialogPosition(FATaskDialog dialog, Window owner)
    {
        if (TopLevel.GetTopLevel(dialog) is not Window window)
        {
            return;
        }

        var scaling = Math.Max(0.5, window.RenderScaling);
        var widthPx = (int)Math.Round(window.Bounds.Width * scaling);
        var heightPx = (int)Math.Round(window.Bounds.Height * scaling);
        if (widthPx <= 0 || heightPx <= 0)
        {
            return;
        }

        var config = GlobalConstants.MainConfig?.Data;
        if (config?.ActionFlowExecutionDelayPositionX is int savedX &&
            config.ActionFlowExecutionDelayPositionY is int savedY)
        {
            var savedPosition = new PixelPoint(savedX, savedY);
            var savedRect = new PixelRect(savedPosition, new PixelSize(widthPx, heightPx));
            if (owner.Screens.All.Any(screen => screen.WorkingArea.Intersects(savedRect)))
            {
                window.Position = savedPosition;
                return;
            }
        }

        CenterDialogOverOwner(dialog, owner);
    }

    private static void RememberDialogPosition(FATaskDialog dialog)
    {
        if (TopLevel.GetTopLevel(dialog) is not Window window ||
            GlobalConstants.MainConfig is not { } config)
        {
            return;
        }

        config.Data.ActionFlowExecutionConfirmationPositionX = window.Position.X;
        config.Data.ActionFlowExecutionConfirmationPositionY = window.Position.Y;
        config.Save();
    }

    private static void RememberDelayDialogPosition(FATaskDialog dialog)
    {
        if (TopLevel.GetTopLevel(dialog) is not Window window ||
            GlobalConstants.MainConfig is not { } config)
        {
            return;
        }

        config.Data.ActionFlowExecutionDelayPositionX = window.Position.X;
        config.Data.ActionFlowExecutionDelayPositionY = window.Position.Y;
        config.Save();
    }

    private sealed record ConfirmationOutcome(object Result, int? DelaySeconds = null);
}
