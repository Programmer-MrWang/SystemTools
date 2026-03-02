using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using SystemTools.Settings;
using SystemTools.Views;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.AdvancedShutdown", "高级计时关机", "\uF357", false)]
public class AdvancedShutdownAction(ILogger<AdvancedShutdownAction> logger) : ActionBase<AdvancedShutdownSettings>
{
    private const string HostProcessName = "ClassIsland.Desktop";

    private readonly ILogger<AdvancedShutdownAction> _logger = logger;

    private static readonly object StateLock = new();
    private static DateTimeOffset _shutdownAt = DateTimeOffset.MinValue;
    private static int _totalScheduledSeconds;
    private static Process? _countdownProcess;
    private static AdvancedShutdownDialog? _activeDialog;
    private static Window? _floatingWindow;
    private static DispatcherTimer? _watchdogTimer;
    private static bool _allowMainDialogClose;
    private static bool _allowFloatingWindowClose;

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("AdvancedShutdownAction OnInvoke 开始");

        if (!IsHostRunning())
        {
            _logger.LogWarning("未检测到 ClassIsland.Desktop.exe，取消启动高级计时关机。");
            StopAllStates();
            return;
        }

        if (!IsPlanActive())
        {
            var configuredMinutes = Math.Max(1, Settings?.Minutes ?? 2);
            ScheduleShutdown(configuredMinutes);
        }

        await ShowDialogAsync();
        await base.OnInvoke();
    }

    private static bool IsHostRunning()
    {
        try
        {
            return Process.GetProcessesByName(HostProcessName).Length > 0;
        }
        catch
        {
            return true;
        }
    }

    private static bool IsPlanActive()
    {
        lock (StateLock)
        {
            return _countdownProcess is { HasExited: false } && _shutdownAt > DateTimeOffset.Now;
        }
    }

    private void ScheduleShutdown(int minutes)
    {
        var safeMinutes = Math.Max(1, minutes);
        var seconds = safeMinutes * 60;

        lock (StateLock)
        {
            _shutdownAt = DateTimeOffset.Now.AddMinutes(safeMinutes);
            _totalScheduledSeconds = seconds;
        }

        StartOrReplaceCountdownProcess(seconds);
        EnsureWatchdogRunning();
    }

    void AssignProgressAnimator(ProgressBar bar,TimeSpan targetTime, TimeSpan totalTime) {
        new Animation {
            Children = {
                new KeyFrame {
                    Cue = new Cue(0),
                    Setters = {
                        new Setter(RangeBase.ValueProperty, 3000.0 * targetTime.TotalMilliseconds/totalTime.TotalMilliseconds)
                    }
                },
                new KeyFrame {
                    Cue = new Cue(1),
                    Setters = {
                        new Setter(RangeBase.ValueProperty, 0.0),
                    }
                }
            },
            Duration = targetTime,
            FillMode = FillMode.Forward
        }.RunAsync(bar);
    }
    
    private void ExtendShutdown(int extendMinutes)
    {
        var safeExtendMinutes = Math.Max(1, extendMinutes);
        DateTimeOffset targetTime;

        lock (StateLock)
        {
            var baseline = _shutdownAt > DateTimeOffset.Now ? _shutdownAt : DateTimeOffset.Now;
            _shutdownAt = baseline.AddMinutes(safeExtendMinutes);
            targetTime = _shutdownAt;
            _totalScheduledSeconds = (int)Math.Ceiling((targetTime - DateTimeOffset.Now).TotalSeconds);
        }

        var totalSeconds = (int)Math.Ceiling((targetTime - DateTimeOffset.Now).TotalSeconds);
        totalSeconds = Math.Max(60, totalSeconds);
        StartOrReplaceCountdownProcess(totalSeconds);
    }

    private void CancelShutdownPlan()
    {
        StopAllStates();
    }

    private void StartOrReplaceCountdownProcess(int seconds)
    {
        StopCountdownProcess();
        var safeSeconds = Math.Max(60, seconds);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c timeout /t {safeSeconds} /nobreak >nul & shutdown /s /t 0",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            _countdownProcess = Process.Start(psi);
            _logger.LogInformation("已启动 Windows 计时关机进程，{Seconds} 秒后执行关机。", safeSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动计时关机进程失败。秒数: {Seconds}", safeSeconds);
            throw;
        }
    }

    private static void StopCountdownProcess()
    {
        try
        {
            if (_countdownProcess is { HasExited: false })
            {
                _countdownProcess.Kill(true);
            }
        }
        catch
        {
        }
        finally
        {
            _countdownProcess?.Dispose();
            _countdownProcess = null;
        }
    }

    private static int GetRemainingSeconds()
    {
        lock (StateLock)
        {
            var remainingSeconds = (int)Math.Ceiling((_shutdownAt - DateTimeOffset.Now).TotalSeconds);
            return Math.Max(0, remainingSeconds);
        }
    }

    private static double BuildCountdownProgress()
    {
        int remaining;
        int total;
        lock (StateLock)
        {
            remaining = Math.Max(0, (int)Math.Ceiling((_shutdownAt - DateTimeOffset.Now).TotalSeconds));
            total = _totalScheduledSeconds;
        }

        if (total <= 0)
        {
            return 0;
        }

        return Math.Clamp(remaining * 100.0 / total, 0, 100);
    }

    private static string BuildCountdownText()
    {
        var remainingSeconds = GetRemainingSeconds();
        var minutes = remainingSeconds / 60;
        var seconds = remainingSeconds % 60;
        return $"距离关机还有{minutes}分{seconds:00}秒";
    }

    private void EnsureWatchdogRunning()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_watchdogTimer != null)
            {
                return;
            }

            _watchdogTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _watchdogTimer.Tick += (_, _) =>
            {
                if (!IsHostRunning())
                {
                    _logger.LogWarning("检测到 ClassIsland.Desktop.exe 已退出，立即停止高级计时关机。");
                    StopAllStates();
                    return;
                }

                if (!IsPlanActive())
                {
                    StopAllStates();
                }
            };
            _watchdogTimer.Start();
        });
    }

    private void StopWatchdog()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _watchdogTimer?.Stop();
            _watchdogTimer = null;
        });
    }

    private void StopAllStates()
    {
        StopCountdownProcess();

        lock (StateLock)
        {
            _shutdownAt = DateTimeOffset.MinValue;
            _totalScheduledSeconds = 0;
        }

        StopWatchdog();

        Dispatcher.UIThread.Post(() =>
        {
            CloseMainDialogProgrammatically();
            CloseFloatingWindowProgrammatically();
        });
    }

    private async Task ShowDialogAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            CloseFloatingWindowProgrammatically();
            await ShowStyledDialogAsync();
        });
    }

    private async Task ShowStyledDialogAsync()
    {
        if (_activeDialog is { IsVisible: true })
        {
            _activeDialog.Activate();
            return;
        }

        var dialog = new AdvancedShutdownDialog
        {
            CanResize = false,
            SystemDecorations = SystemDecorations.Full
        };
        _activeDialog = dialog;

        dialog.Closing += (_, e) =>
        {
            if (!_allowMainDialogClose && IsPlanActive())
            {
                e.Cancel = true;
            }
        };

        var textBlock = dialog.CountdownTextBlock ?? throw new InvalidOperationException("CountdownTextBlockElement 未找到");
        var progressBar = dialog.CountdownProgressBar ?? throw new InvalidOperationException("CountdownProgressBarElement 未找到");
        var readButton = dialog.ReadButton ?? throw new InvalidOperationException("ReadButtonElement 未找到");
        var cancelPlanButton = dialog.CancelPlanButton ?? throw new InvalidOperationException("CancelPlanButtonElement 未找到");
        var extendButton = dialog.ExtendButton ?? throw new InvalidOperationException("ExtendButtonElement 未找到");

        textBlock.Text = BuildCountdownText();
        progressBar.Value = BuildCountdownProgress();

        var countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        countdownTimer.Tick += (_, _) =>
        {
            textBlock.Text = BuildCountdownText();
            //progressBar.Value = BuildCountdownProgress();

            if (!IsPlanActive())
            {
                CloseMainDialogProgrammatically();
            }
        };
        AssignProgressAnimator(progressBar,_shutdownAt - DateTimeOffset.Now, TimeSpan.FromSeconds(_totalScheduledSeconds));
        countdownTimer.Start();

        dialog.Closed += (_, _) =>
        {
            countdownTimer.Stop();
            if (ReferenceEquals(_activeDialog, dialog))
            {
                _activeDialog = null;
            }

            if (IsPlanActive())
            {
                ShowOrUpdateFloatingWindow();
            }
        };

        readButton.Click += (_, _) =>
        {
            CloseMainDialogProgrammatically();
            ShowOrUpdateFloatingWindow();
        };

        cancelPlanButton.Click += (_, _) => CancelShutdownPlan();

        extendButton.Click += async (_, _) =>
        {
            var extendMinutes = await ShowExtendInputDialogAsync(dialog);
            if (extendMinutes.HasValue)
            {
                ExtendShutdown(extendMinutes.Value);
                CloseMainDialogProgrammatically();
                ShowOrUpdateFloatingWindow();
            }
        };

        dialog.Show();
        dialog.Activate();
        await Task.CompletedTask;
    }

    private void ShowOrUpdateFloatingWindow()
    {
        if (!IsPlanActive())
        {
            CloseFloatingWindowProgrammatically();
            return;
        }

        if (_floatingWindow is { IsVisible: true })
        {
            return;
        }

        var tipButton = new Button
        {
            Content = BuildCountdownText() + "  点此返回设置",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Avalonia.Media.Brushes.Transparent,
            Foreground = Avalonia.Media.Brushes.White
        };

        tipButton.Click += async (_, _) => await ShowDialogAsync();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            if (!IsPlanActive())
            {
                CloseFloatingWindowProgrammatically();
                return;
            }

            tipButton.Content = BuildCountdownText() + "  点此返回设置";
        };

        var floatWindow = new Window
        {
            Width = 320,
            Height = 56,
            CanResize = false,
            Topmost = true,
            ShowInTaskbar = false,
            SystemDecorations = SystemDecorations.None,
            Content = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 6),
                Background = Avalonia.Media.Brushes.Black,
                Opacity = 0.92,
                Child = tipButton
            }
        };

        floatWindow.Opened += (_, _) => PinFloatingWindowTopRight(floatWindow);
        floatWindow.PositionChanged += (_, _) => PinFloatingWindowTopRight(floatWindow);
        floatWindow.Closing += (_, e) =>
        {
            if (!_allowFloatingWindowClose && IsPlanActive())
            {
                e.Cancel = true;
                PinFloatingWindowTopRight(floatWindow);
            }
        };
        floatWindow.Closed += (_, _) =>
        {
            timer.Stop();
            if (ReferenceEquals(_floatingWindow, floatWindow))
            {
                _floatingWindow = null;
            }
        };

        _floatingWindow = floatWindow;
        floatWindow.Show();
        timer.Start();
    }

    private static void PinFloatingWindowTopRight(Window window)
    {
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var x = area.X + area.Width - (int)window.Width - 12;
        var y = area.Y + 12;
        var target = new PixelPoint(Math.Max(area.X, x), Math.Max(area.Y, y));

        if (window.Position != target)
        {
            window.Position = target;
        }
    }

    private void CloseMainDialogProgrammatically()
    {
        if (_activeDialog is not { } dialog)
        {
            return;
        }

        _allowMainDialogClose = true;
        dialog.Close();
        _allowMainDialogClose = false;
        _activeDialog = null;
    }

    private void CloseFloatingWindowProgrammatically()
    {
        if (_floatingWindow is not { } window)
        {
            return;
        }

        _allowFloatingWindowClose = true;
        window.Close();
        _allowFloatingWindowClose = false;
        _floatingWindow = null;
    }

    private static async Task<int?> ShowExtendInputDialogAsync(Window owner)
    {
        var dialog = new ExtendShutdownDialog();
        await dialog.ShowDialog(owner);
        return dialog.ResultMinutes;
    }
}
