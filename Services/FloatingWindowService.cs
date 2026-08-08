using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Controls;
using ClassIsland.Shared;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using SystemTools.ConfigHandlers;
using SystemTools.Triggers;
using System.Runtime.InteropServices;
using LiquidGlassAvaloniaUI;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using DrawingRectangle = System.Drawing.Rectangle;

namespace SystemTools.Services;

public class FloatingWindowService
{
    private const uint EventSystemForeground = 0x0003;
    private const uint EventObjectReorder = 0x8004;
    private const uint WinEventOutOfContext = 0;
    private const uint WinEventSkipOwnProcess = 2;
    private static readonly HWND HwndBottom = new(1);
    private static readonly HWND HwndTopmost = new(-1);
    private const int WhMouseLl = 14;
    private const int WmMouseMove = 0x0200;
    private const int WmLButtonDown = 0x0201;
    private const int WmRButtonDown = 0x0204;
    private const ulong MiWpSignatureMask = 0xFFFFFF00UL;
    private const ulong MiWpSignature = 0xFF515700UL;
    private const double LiquidGlassOuterGutter = 10.0;
    private const int FollowClassIslandTheme = 0;
    private const int LightTheme = 1;
    private const int DarkTheme = 2;
    private const int AdaptiveBackgroundTheme = 3;
    private const int AdaptiveThemeRefreshStride = 8;
    private static readonly TimeSpan TouchLikeMouseGracePeriod = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan GlassButtonStateRefreshDelay = TimeSpan.FromMilliseconds(220);

    private readonly MainConfigHandler _configHandler;
    private readonly FloatingWindowProfileManager _profileManager;
    private readonly MainWindowBackgroundCaptureService _backgroundCaptureService;
    private readonly Dictionary<FloatingWindowTrigger, FloatingWindowEntry> _entries = new();
    private Window? _window;
    private Grid? _windowRoot;
    private StackPanel? _stackPanel;
    private Border? _windowContainer;
    private Border? _liquidGlassBackdropClip;
    private LiquidGlassSurface? _liquidGlassSurface;

    private readonly DispatcherTimer _liquidGlassCaptureTimer;
    private readonly DispatcherTimer _deferredButtonRefreshTimer;
    private WriteableBitmap? _liquidGlassBackdrop;
    private WriteableBitmap? _liquidGlassSpareBackdrop;
    private IDisposable? _continuousCaptureLease;
    private IDisposable? _windowCaptureExclusionLease;
    private CancellationTokenSource? _glassCaptureCancellation;
    private Task? _glassCaptureTask;
    private long _glassCaptureGeneration;
    private bool _liquidGlassCaptureRefreshPending;
    private ThemeVariant? _adaptiveBackgroundThemeVariant;
    private int _adaptiveThemeRefreshCount;
    private bool _windowBoundsClampQueued;
    private bool _pointerPressed;
    private bool _dragInitiated;
    private Point _pointerDownPoint;
    private PointerPressedEventArgs? _lastPressedArgs;
    private bool _isThemeSubscribed;
    private readonly Dictionary<string, double> _buttonWidthCache = new();
    private int _lastButtonLayoutStyle = -1;
    private double _lastButtonLayoutScale = double.NaN;
    private bool _allowWindowClose;
    private bool _restoringFromMinimized;
    private bool _isStarted;
    private bool _isStopped;
    private bool _isTouchDeviceDetected;
    private bool _touchDragAllowed;
    private PixelPoint _touchDragStartScreenPoint;
    private PixelPoint _touchDragStartWindowPosition;
    private Border? _touchDragHandle;
    private DateTime _lastTouchGeneratedMouseEventAt = DateTime.MinValue;
    private IntPtr _foregroundHook;
    private IntPtr _reorderHook;
    private WinEventProc? _winEventProc;
    private IntPtr _mouseHook;
    private LowLevelMouseProc? _lowLevelMouseProc;
    private ILessonsService? _lessonsService;
    private DispatcherTimer LayerRecheck50MsTimer { get; } = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private DispatcherTimer LayerRecheck1MsTimer { get; } = new() { Interval = TimeSpan.FromMilliseconds(1) };

    private delegate void WinEventProc(IntPtr hWinEventHook, uint @event, IntPtr hwnd, int idObject, int idChild, uint idEventThread,
        uint dwmsEventTime);

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    public event EventHandler? EntriesChanged;

    public FloatingWindowService(
        MainConfigHandler configHandler,
        FloatingWindowProfileManager profileManager,
        MainWindowBackgroundCaptureService backgroundCaptureService)
    {
        _configHandler = configHandler;
        _profileManager = profileManager;
        _backgroundCaptureService = backgroundCaptureService;
        _liquidGlassCaptureTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(_configHandler.Data.FloatingWindowLiquidGlass.BackdropRefreshIntervalMs),
            DispatcherPriority.Background,
            (_, _) => UpdateLiquidGlassCaptureLoop());
        _deferredButtonRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = GlassButtonStateRefreshDelay
        };
        _deferredButtonRefreshTimer.Tick += OnDeferredButtonRefreshTimerTick;
    }

    public IReadOnlyList<FloatingWindowEntry> Entries => _entries.Values.ToList();

    public FloatingWindowProfileManager ProfileManager => _profileManager;

    public void Start()
    {
        if (_isStarted)
        {
            return;
        }

        _isStarted = true;
        _isStopped = false;
        Dispatcher.UIThread.Post(() =>
        {
            if (_isStopped)
            {
                return;
            }

            _profileManager.LoadProfile(_configHandler.Data.CurrentFloatingWindowProfile);
            EnsureWindow();
            EnsureLayerRecheckHooks();
            EnsureGlobalInputHooks();
            SubscribeThemeChanged();
            _configHandler.Data.PropertyChanged += OnConfigPropertyChanged;
            ApplyVisibility();
            RefreshLayerRecheckMode();
            RecheckWindowLayer();
            RefreshWindowButtons();
        });
    }

    public void Stop()
    {
        if (!_isStarted)
        {
            return;
        }

        _isStarted = false;
        _isStopped = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (_window != null)
            {
                _allowWindowClose = true;
                _window.Close();
            }

            DiscardWindowState();

            LayerRecheck50MsTimer.Stop();
            LayerRecheck1MsTimer.Stop();
            RemoveLayerRecheckHooks();
            RemoveGlobalInputHooks();
            UnsubscribeThemeChanged();
            _configHandler.Data.PropertyChanged -= OnConfigPropertyChanged;
            _deferredButtonRefreshTimer.Stop();
            StopLiquidGlassCapture();
        });
    }

    public void RegisterTrigger(FloatingWindowTrigger trigger)
    {
        var isExistingTrigger = _entries.ContainsKey(trigger);
        _entries[trigger] = CreateEntry(trigger);

        PruneButtonWidthCache();
        NotifyEntriesChanged(isExistingTrigger);
    }

    public void EnsureUniqueButtonIds()
    {
        var usedButtonIds = new HashSet<string>();
        var changed = false;

        foreach (var trigger in _entries.Keys.ToList())
        {
            var oldButtonId = trigger.GetButtonId();
            var buttonId = trigger.GetUniqueButtonId(usedButtonIds.Contains);
            usedButtonIds.Add(buttonId);
            _entries[trigger] = CreateEntry(trigger);

            if (!string.Equals(oldButtonId, buttonId, StringComparison.Ordinal))
            {
                changed = true;
            }
        }

        if (changed)
        {
            PruneButtonWidthCache();
        }
    }

    private FloatingWindowEntry CreateEntry(FloatingWindowTrigger trigger)
    {
        var buttonId = trigger.GetUniqueButtonId(id => _entries.Any(x =>
            !ReferenceEquals(x.Key, trigger) && string.Equals(x.Value.ButtonId, id, StringComparison.Ordinal)));

        return new FloatingWindowEntry(
            buttonId,
            trigger.GetIcon(),
            trigger.GetButtonName(),
            trigger.ShouldUseRevertStyle(),
            trigger.IsRevertEnabled(),
            trigger.GetLayoutButtonName(),
            trigger.TriggerFromFloatingWindow,
            trigger.CancelIsOnState);
    }

    public void UnregisterTrigger(FloatingWindowTrigger trigger)
    {
        if (_entries.Remove(trigger))
        {
            PruneButtonWidthCache();
            NotifyEntriesChanged();
        }
    }

    public void UpdateWindowState()
    {
        if (_isStopped) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_isStopped) return;
            ApplyVisibility();
            RefreshLayerRecheckMode();
            RecheckWindowLayer();
            RefreshWindowButtons();
        });
    }

    private void NotifyEntriesChanged(bool preserveGlassButtonAnimation = false)
    {
        EntriesChanged?.Invoke(this, EventArgs.Empty);
        if (_isStopped) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_isStopped) return;
            ApplyVisibility();
            RecheckWindowLayer();
            if (preserveGlassButtonAnimation && IsLiquidGlassRequested())
            {
                _deferredButtonRefreshTimer.Stop();
                _deferredButtonRefreshTimer.Start();
            }
            else
            {
                _deferredButtonRefreshTimer.Stop();
                RefreshWindowButtons();
            }
        });
    }

    private void OnDeferredButtonRefreshTimerTick(object? sender, EventArgs e)
    {
        _deferredButtonRefreshTimer.Stop();
        if (!_isStopped)
        {
            RefreshWindowButtons();
        }
    }

    private void SubscribeThemeChanged()
    {
        if (_isThemeSubscribed || Application.Current == null)
        {
            return;
        }

        Application.Current.PropertyChanged += OnApplicationPropertyChanged;
        _isThemeSubscribed = true;
    }

    private void UnsubscribeThemeChanged()
    {
        if (!_isThemeSubscribed || Application.Current == null)
        {
            return;
        }

        Application.Current.PropertyChanged -= OnApplicationPropertyChanged;
        _isThemeSubscribed = false;
    }

    private void OnApplicationPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (string.Equals(e.Property?.Name, "ActualThemeVariant", StringComparison.Ordinal)
            && _configHandler.Data.FloatingWindowTheme == FollowClassIslandTheme)
        {
            Dispatcher.UIThread.Post(() =>
            {
                RefreshWindowButtons();
                ApplyLiquidGlassAppearance();
            });
        }
    }

    private ThemeVariant ResolveWindowThemeVariant()
    {
        return _configHandler.Data.FloatingWindowTheme switch
        {
            LightTheme => ThemeVariant.Light,
            DarkTheme => ThemeVariant.Dark,
            AdaptiveBackgroundTheme => _adaptiveBackgroundThemeVariant
                                       ?? Application.Current?.ActualThemeVariant
                                       ?? ThemeVariant.Dark,
            _ => _window?.ActualThemeVariant ?? Application.Current?.ActualThemeVariant ?? ThemeVariant.Dark
        };
    }

    private bool IsLightTheme()
    {
        return ResolveWindowThemeVariant() == ThemeVariant.Light;
    }

    /// <summary>
    /// 设置悬浮窗主题
    /// </summary>
    /// <param name="theme">0=跟随 ClassIsland, 1=浅色, 2=深色, 3=自适应背景</param>
    public void SetWindowTheme(int theme)
    {
        var normalized = theme is LightTheme or DarkTheme or AdaptiveBackgroundTheme
            ? theme
            : FollowClassIslandTheme;
        if (_configHandler.Data.FloatingWindowTheme == normalized)
        {
            return;
        }

        _configHandler.Data.FloatingWindowTheme = normalized;
        _configHandler.Save();
        Dispatcher.UIThread.Post(RefreshWindowButtons);
    }

    /// <summary>
    /// 切换到下一个悬浮窗主题
    /// </summary>
    public void ToggleWindowTheme()
    {
        var next = (_configHandler.Data.FloatingWindowTheme + 1) % 4;
        SetWindowTheme(next);
    }

    private void EnsureWindow()
    {
        if (_window != null || _isStopped)
        {
            return;
        }

        _allowWindowClose = false;
        _stackPanel = new StackPanel { Margin = new Thickness(6), Spacing = 6 };
        _liquidGlassBackdropClip = new Border
        {
            IsVisible = false,
            IsHitTestVisible = false,
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = IsLiquidGlassRequested()
                ? new Thickness(-LiquidGlassOuterGutter)
                : default,
            Background = Brushes.Transparent
        };
        _liquidGlassSurface = new LiquidGlassSurface
        {
            IsVisible = false,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _windowContainer = new Border
        {
            Background = TryParseColor("#CC1F1F1F") ??
                         new SolidColorBrush(Color.FromArgb(0xCC, 0x1F, 0x1F, 0x1F)),
            CornerRadius = new CornerRadius(8),
            Child = _stackPanel
        };
        LiquidGlassBackdrop.SetIsExcludedFromCapture(_windowContainer, true);
        _windowRoot = new Grid
        {
            Margin = IsLiquidGlassRequested()
                ? new Thickness(LiquidGlassOuterGutter)
                : default,
            Children =
            {
                _liquidGlassBackdropClip,
                _liquidGlassSurface,
                _windowContainer
            }
        };
        _window = new Window
        {
            Width = 64,
            Height = 64,
            ShowActivated = false,
            Topmost = _configHandler.Data.FloatingWindowLayer == 1,
            WindowDecorations = WindowDecorations.None,
            Background = Brushes.Transparent,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            CanResize = false,
            ShowInTaskbar = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = _windowRoot
        };

        _window.Loaded += OnWindowLoaded;
        _window.Opened += OnWindowOpened;
        _window.PositionChanged += OnWindowPositionChanged;
        _window.SizeChanged += OnWindowSizeChanged;
        _window.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, true);
        _window.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, true);
        _window.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, true);
        _window.Closing += (_, e) =>
        {
            if (!_allowWindowClose)
            {
                e.Cancel = true;
                // 不在 Closing 事件中调用 Show()，窗口可能处于关闭过程中
            }
            else
            {
                StopLiquidGlassCapture();
            }
        };
        _window.PropertyChanged += OnWindowPropertyChanged;

        _window.Show();
    }

    private void OnWindowPositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (!ReferenceEquals(sender, _window))
        {
            return;
        }

        if (IsBackgroundCaptureRequested())
        {
            _liquidGlassCaptureRefreshPending = true;
            QueueLiquidGlassBackdropCapture();
        }
    }

    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _window))
        {
            return;
        }

        if (IsBackgroundCaptureRequested())
        {
            _liquidGlassCaptureRefreshPending = true;
            QueueLiquidGlassBackdropCapture();
        }

        QueueWindowBoundsClamp();
    }

    private void QueueWindowBoundsClamp()
    {
        if (_window == null || _windowBoundsClampQueued)
        {
            return;
        }

        var targetWindow = _window;
        _windowBoundsClampQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _windowBoundsClampQueued = false;
            if (!ReferenceEquals(_window, targetWindow))
            {
                return;
            }

            var clamped = ClampToVisibleScreen(targetWindow.Position);
            if (clamped != targetWindow.Position)
            {
                targetWindow.Position = clamped;
                SavePosition(clamped);
            }
        }, DispatcherPriority.Background);
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_window == null || _restoringFromMinimized)
        {
            return;
        }

        if (e.Property == Window.WindowStateProperty && _window.WindowState == WindowState.Minimized)
        {
            RestoreWindowFromMinimized();
        }
    }

    private void RestoreWindowFromMinimized()
    {
        if (_window == null || _restoringFromMinimized || _isStopped)
        {
            return;
        }

        _restoringFromMinimized = true;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (_window == null || _isStopped)
                {
                    return;
                }

                if (!_window.IsVisible)
                {
                    try { _window.Show(); }
                    catch (InvalidOperationException)
                    {
                        DiscardWindowState();
                    }
                }

                if (_window != null)
                {
                    _window.WindowState = WindowState.Normal;
                }
            }
            finally
            {
                _restoringFromMinimized = false;
            }
        }, DispatcherPriority.Background);
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        if (_window == null || !ReferenceEquals(sender, _window))
        {
            return;
        }

        EnsureWindowPositionVisibleOnStartup();
        RecheckWindowLayer();
        ApplyLiquidGlassAppearance();
        UpdateLiquidGlassCaptureLoop();
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _window) || _isStopped)
        {
            return;
        }

        ApplyLiquidGlassAppearance();
        UpdateLiquidGlassCaptureLoop();
    }

    private void OnConfigPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainConfigData.FloatingWindowTheme))
        {
            _adaptiveBackgroundThemeVariant = null;
            _adaptiveThemeRefreshCount = 0;
        }

        if (e.PropertyName is nameof(MainConfigData.FloatingWindowAppearanceStyle)
            or nameof(MainConfigData.FloatingWindowLiquidGlass)
            or nameof(MainConfigData.FloatingWindowGlassButtonScaleDip)
            or nameof(MainConfigData.FloatingWindowOpacity)
            or nameof(MainConfigData.FloatingWindowTheme)
            or nameof(MainConfigData.FloatingWindowScale)
            or nameof(MainConfigData.FloatingWindowIconSize)
            or nameof(MainConfigData.FloatingWindowTextSize)
            or nameof(MainConfigData.FloatingWindowShadowEnabled)
            or nameof(MainConfigData.FloatingWindowDragHandleAlwaysVisible))
        {
            Dispatcher.UIThread.Post(() =>
            {
                RefreshWindowButtons();
                ApplyLiquidGlassAppearance();
                UpdateLiquidGlassCaptureLoop();
            });
        }
    }

    private bool IsLiquidGlassRequested()
    {
        return _configHandler.Data.FloatingWindowAppearanceStyle == 1;
    }

    private bool IsAdaptiveBackgroundThemeRequested()
    {
        return _configHandler.Data.FloatingWindowTheme == AdaptiveBackgroundTheme;
    }

    private bool IsBackgroundCaptureRequested()
    {
        return IsLiquidGlassRequested() || IsAdaptiveBackgroundThemeRequested();
    }

    private void UpdateLiquidGlassCaptureLoop()
    {
        if (_window == null)
        {
            return;
        }

        var settings = _configHandler.Data.FloatingWindowLiquidGlass;
        _liquidGlassCaptureTimer.Interval = TimeSpan.FromMilliseconds(
            Math.Max(5, settings.BackdropRefreshIntervalMs));

        var shouldCapture = !_isStopped && _window.IsVisible && IsBackgroundCaptureRequested();
        if (!shouldCapture)
        {
            StopLiquidGlassCapture();
            ApplyLiquidGlassAppearance();
            return;
        }

        if (!IsLiquidGlassRequested())
        {
            ReleaseLiquidGlassBackdropImages();
        }

        // The native handle and the SizeToContent client size can become available one
        // dispatcher turn after Loaded. Keep the cadence timer alive so the first frame
        // is retried instead of permanently falling back to the classic background.
        if (!_liquidGlassCaptureTimer.IsEnabled)
        {
            _liquidGlassCaptureTimer.Start();
        }

        var windowHandle = _window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        _continuousCaptureLease ??= _backgroundCaptureService.BeginContinuousCapture();
        if (_windowCaptureExclusionLease is null)
        {
            _windowCaptureExclusionLease = _backgroundCaptureService.BeginExcludedWindowCapture(windowHandle);
        }

        QueueLiquidGlassBackdropCapture();
    }

    private void QueueLiquidGlassBackdropCapture()
    {
        if (_isStopped || _window == null || !_window.IsVisible || !IsBackgroundCaptureRequested() ||
            _glassCaptureTask is not null)
        {
            return;
        }

        var captureWindow = _window;
        var handle = captureWindow.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero || !TryGetLiquidGlassCaptureArea(captureWindow, out var area))
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        var generation = _glassCaptureGeneration;
        _liquidGlassCaptureRefreshPending = false;
        _glassCaptureCancellation = cancellation;
        _glassCaptureTask = CaptureLiquidGlassBackdropAsync(
            captureWindow,
            handle,
            area,
            generation,
            cancellation.Token);
        var captureTask = _glassCaptureTask;
        _ = captureTask.ContinueWith(_ => Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_glassCaptureTask, captureTask))
            {
                return;
            }

            _glassCaptureTask = null;
            _glassCaptureCancellation?.Dispose();
            _glassCaptureCancellation = null;
            var shouldContinue = _window != null && _window.IsVisible && IsBackgroundCaptureRequested() &&
                                 !_isStopped;
            if (generation != _glassCaptureGeneration || !shouldContinue)
            {
                ReleaseLiquidGlassBackdrops();
                ApplyLiquidGlassAppearance();
                if (shouldContinue)
                {
                    UpdateLiquidGlassCaptureLoop();
                }
            }
            else if (_liquidGlassCaptureRefreshPending)
            {
                QueueLiquidGlassBackdropCapture();
            }
        }), TaskScheduler.Default);
    }

    private bool TryGetLiquidGlassCaptureArea(Window captureWindow, out DrawingRectangle area)
    {
        area = default;
        if (captureWindow.ClientSize.Width <= 0 || captureWindow.ClientSize.Height <= 0)
        {
            return false;
        }

        var scaling = Math.Max(0.1, captureWindow.RenderScaling);
        var width = Math.Max(1, (int)Math.Ceiling(captureWindow.ClientSize.Width * scaling));
        var height = Math.Max(1, (int)Math.Ceiling(captureWindow.ClientSize.Height * scaling));
        area = new DrawingRectangle(captureWindow.Position.X, captureWindow.Position.Y, width, height);
        return true;
    }

    private async Task CaptureLiquidGlassBackdropAsync(
        Window captureWindow,
        IntPtr windowHandle,
        DrawingRectangle area,
        long generation,
        CancellationToken cancellationToken)
    {
        try
        {
            using var frame = await Task.Run(
                () => _backgroundCaptureService.CaptureAreaAsync(area, windowHandle, cancellationToken),
                cancellationToken);
            if (frame is null || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!ReferenceEquals(_window, captureWindow) || !captureWindow.IsVisible ||
                    generation != _glassCaptureGeneration ||
                    cancellationToken.IsCancellationRequested ||
                    !IsBackgroundCaptureRequested())
                {
                    return;
                }

                if (!TryGetLiquidGlassCaptureArea(captureWindow, out var currentArea) ||
                    !currentArea.Equals(area))
                {
                    _liquidGlassCaptureRefreshPending = true;
                    return;
                }

                var adaptiveThemeChanged = UpdateAdaptiveBackgroundTheme(frame);
                if (!IsLiquidGlassRequested())
                {
                    if (adaptiveThemeChanged)
                    {
                        RefreshWindowButtons();
                        ApplyLiquidGlassAppearance();
                    }

                    return;
                }

                var bitmap = LiquidGlassBackdropFactory.Update(frame, _liquidGlassSpareBackdrop);
                if (bitmap is null)
                {
                    if (adaptiveThemeChanged)
                    {
                        RefreshWindowButtons();
                        ApplyLiquidGlassAppearance();
                    }

                    return;
                }

                var previous = _liquidGlassBackdrop;
                _liquidGlassBackdrop = bitmap;
                _liquidGlassSpareBackdrop = previous;
                if (_liquidGlassBackdropClip != null)
                {
                    _liquidGlassBackdropClip.Background = new ImageBrush
                    {
                        Source = bitmap,
                        Stretch = Stretch.Fill
                    };
                }

                if (adaptiveThemeChanged)
                {
                    RefreshWindowButtons();
                }

                ApplyLiquidGlassAppearance();
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            // Keep the last frame and the frosted fallback visible. A capture failure should not
            // break trigger interaction or the window's drag lifecycle.
        }
    }

    private bool UpdateAdaptiveBackgroundTheme(MainWindowBackgroundFrame frame)
    {
        if (!IsAdaptiveBackgroundThemeRequested())
        {
            _adaptiveThemeRefreshCount = 0;
            return false;
        }

        _adaptiveThemeRefreshCount++;
        if (_adaptiveThemeRefreshCount < AdaptiveThemeRefreshStride)
        {
            return false;
        }

        _adaptiveThemeRefreshCount = 0;
        var luminance = BackgroundLuminanceCalculator.CalculateAverage(frame);
        if (luminance == null)
        {
            return false;
        }

        var previousTheme = ResolveWindowThemeVariant();
        _adaptiveBackgroundThemeVariant = luminance < BackgroundLuminanceCalculator.DarkThreshold
            ? ThemeVariant.Dark
            : ThemeVariant.Light;
        return !Equals(previousTheme, _adaptiveBackgroundThemeVariant);
    }

    private void StopLiquidGlassCapture()
    {
        _liquidGlassCaptureTimer.Stop();
        _liquidGlassCaptureRefreshPending = false;
        _adaptiveThemeRefreshCount = 0;
        _glassCaptureGeneration++;
        _glassCaptureCancellation?.Cancel();
        if (_glassCaptureTask is not null)
        {
            return;
        }

        _glassCaptureCancellation?.Dispose();
        _glassCaptureCancellation = null;
        ReleaseLiquidGlassBackdrops();
    }

    private void ReleaseLiquidGlassBackdrops()
    {
        _windowCaptureExclusionLease?.Dispose();
        _windowCaptureExclusionLease = null;
        _continuousCaptureLease?.Dispose();
        _continuousCaptureLease = null;
        ReleaseLiquidGlassBackdropImages();
    }

    private void ReleaseLiquidGlassBackdropImages()
    {
        if (_liquidGlassBackdropClip != null)
        {
            _liquidGlassBackdropClip.Background = Brushes.Transparent;
        }

        _liquidGlassBackdrop?.Dispose();
        _liquidGlassBackdrop = null;
        _liquidGlassSpareBackdrop?.Dispose();
        _liquidGlassSpareBackdrop = null;
    }

    private void ApplyLiquidGlassAppearance()
    {
        if (_windowContainer == null || _liquidGlassSurface == null || _liquidGlassBackdropClip == null)
        {
            return;
        }

        var config = _configHandler.Data;
        var settings = config.FloatingWindowLiquidGlass;
        var scale = Math.Clamp(config.FloatingWindowScale, 0.5, 2.0);
        var useLiquidGlass = IsLiquidGlassRequested() && _liquidGlassBackdrop is not null;
        var radius = new CornerRadius(Math.Max(0, settings.CornerRadius) * scale);
        var opacity = Math.Clamp(config.FloatingWindowOpacity, 10, 100) / 100.0;

        var glassRequested = IsLiquidGlassRequested();
        if (_windowRoot != null)
        {
            _windowRoot.Margin = glassRequested ? new Thickness(LiquidGlassOuterGutter) : default;
        }

        // Extend the captured image through the transparent outer gutter so the lens can
        // sample real desktop pixels around the material edge.
        _liquidGlassBackdropClip.Margin = glassRequested
            ? new Thickness(-LiquidGlassOuterGutter)
            : default;
        _liquidGlassBackdropClip.CornerRadius = default;
        _liquidGlassSurface.CornerRadius = radius;
        _liquidGlassBackdropClip.IsVisible = useLiquidGlass;
        _liquidGlassSurface.IsVisible = useLiquidGlass;
        _liquidGlassBackdropClip.Opacity = 1;
        _liquidGlassSurface.Opacity = opacity;
        _windowContainer.CornerRadius = glassRequested ? radius : new CornerRadius(8);
        _windowContainer.Background = useLiquidGlass
            ? Brushes.Transparent
            : CreateFallbackWindowBrush(IsLightTheme(), opacity);
        _windowContainer.BoxShadow = useLiquidGlass || !config.FloatingWindowShadowEnabled
            ? default
            : CreateFallbackShadow(IsLightTheme(), scale);

        ApplyLiquidGlassSettings(_liquidGlassSurface, settings, scale, shadowEnabled: false);
    }

    private static IBrush CreateFallbackWindowBrush(bool isLightTheme, double opacity)
    {
        var alpha = (byte)Math.Round(255 * opacity);
        return new SolidColorBrush(isLightTheme
            ? Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(alpha, 0x1F, 0x1F, 0x1F));
    }

    private static BoxShadows CreateFallbackShadow(bool isLightTheme, double scale)
    {
        return new BoxShadows(new BoxShadow
        {
            OffsetX = 0,
            OffsetY = 6 * scale,
            Blur = 18 * scale,
            Spread = 0,
            Color = isLightTheme ? Color.Parse("#28000000") : Color.Parse("#60000000")
        });
    }

    private static void ApplyLiquidGlassSettings(
        LiquidGlassSurface surface,
        LiquidGlassSettings settings,
        double scale,
        bool shadowEnabled)
    {
        surface.BackdropZoom = settings.BackdropZoom;
        surface.BackdropOffset = new Vector(settings.BackdropOffsetX, settings.BackdropOffsetY);
        surface.RefractionHeight = settings.RefractionHeight * scale;
        surface.RefractionAmount = settings.RefractionAmount * scale;
        surface.DepthEffect = settings.DepthEffect;
        surface.ChromaticAberration = settings.ChromaticAberration;
        surface.BlurRadius = settings.BlurRadius * scale;
        surface.Vibrancy = settings.Vibrancy;
        surface.Brightness = settings.Brightness;
        surface.Contrast = settings.Contrast;
        surface.ExposureEv = settings.ExposureEv;
        surface.GammaPower = settings.GammaPower;
        surface.BackdropOpacity = settings.BackdropOpacity;
        surface.TintColor = ParseGlassColor(settings.TintColor, Colors.Transparent);
        surface.SurfaceColor = ParseGlassColor(settings.SurfaceColor, Colors.Transparent);
        surface.ProgressiveBlurEnabled = settings.ProgressiveBlurEnabled;
        surface.ProgressiveBlurStart = settings.ProgressiveBlurStart;
        surface.ProgressiveBlurEnd = settings.ProgressiveBlurEnd;
        surface.ProgressiveTintColor = ParseGlassColor(settings.ProgressiveTintColor, Colors.Transparent);
        surface.ProgressiveTintIntensity = settings.ProgressiveTintIntensity;
        surface.AdaptiveLuminanceEnabled = settings.AdaptiveLuminanceEnabled;
        surface.AdaptiveLuminanceUpdateIntervalMs = settings.AdaptiveLuminanceUpdateIntervalMs;
        surface.AdaptiveLuminanceSmoothing = settings.AdaptiveLuminanceSmoothing;
        surface.HighlightEnabled = settings.HighlightEnabled;
        surface.HighlightWidth = settings.HighlightWidth;
        surface.HighlightBlurRadius = settings.HighlightBlurRadius;
        surface.HighlightOpacity = settings.HighlightOpacity;
        surface.HighlightAngle = settings.HighlightAngle;
        surface.HighlightFalloff = settings.HighlightFalloff;
        surface.ShadowEnabled = shadowEnabled && settings.ShadowEnabled;
        surface.ShadowRadius = settings.ShadowRadius * scale;
        surface.ShadowOffset = new Vector(settings.ShadowOffsetX * scale, settings.ShadowOffsetY * scale);
        surface.ShadowColor = ParseGlassColor(settings.ShadowColor, Color.FromArgb(26, 0, 0, 0));
        surface.ShadowOpacity = settings.ShadowOpacity;
        surface.InnerShadowEnabled = settings.InnerShadowEnabled;
        surface.InnerShadowRadius = settings.InnerShadowRadius * scale;
        surface.InnerShadowOffset = new Vector(settings.InnerShadowOffsetX * scale, settings.InnerShadowOffsetY * scale);
        surface.InnerShadowColor = ParseGlassColor(settings.InnerShadowColor, Color.FromArgb(38, 0, 0, 0));
        surface.InnerShadowOpacity = settings.InnerShadowOpacity;
    }

    private static Color ParseGlassColor(string? value, Color fallback) =>
        Color.TryParse(value, out var color) ? color : fallback;

    private bool _rulesetHidingWindow = false;
    private readonly HashSet<string> _rulesetHiddenButtons = new();
    private readonly HashSet<int> _rulesetHiddenRows = new();

    private void CheckFloatingWindowRuleset()
    {
        var profile = _profileManager.CurrentProfile;
        if (!_configHandler.Data.FloatingWindowRulesetEnabled)
        {
            if (_rulesetHidingWindow)
            {
                _rulesetHidingWindow = false;
                ApplyVisibility();
            }
            return;
        }

        var rulesetService = IAppHost.TryGetService<IRulesetService>();
        if (rulesetService == null)
        {
            return;
        }

        var isSatisfied = rulesetService.IsRulesetSatisfied(_configHandler.Data.FloatingWindowRuleset);
        var shouldHide = isSatisfied;

        if (shouldHide != _rulesetHidingWindow)
        {
            _rulesetHidingWindow = shouldHide;
            ApplyVisibility();
        }
    }

    private void CheckButtonRulesets()
    {
        var profile = _profileManager.CurrentProfile;
        var rulesetService = IAppHost.TryGetService<IRulesetService>();
        if (rulesetService == null)
        {
            return;
        }

        var changed = false;
        foreach (var entry in _entries.Values)
        {
            if (!profile.FloatingWindowButtonRulesets.TryGetValue(entry.ButtonId, out var config))
            {
                continue;
            }

            var shouldHide = false;
            if (!config.IsVisible)
            {
                shouldHide = true;
            }
            else if (config.HideOnRule)
            {
                shouldHide = rulesetService.IsRulesetSatisfied(config.HidingRules);
            }

            var wasHidden = _rulesetHiddenButtons.Contains(entry.ButtonId);
            if (shouldHide != wasHidden)
            {
                if (shouldHide)
                {
                    _rulesetHiddenButtons.Add(entry.ButtonId);
                }
                else
                {
                    _rulesetHiddenButtons.Remove(entry.ButtonId);
                }
                changed = true;
            }
        }

        if (changed)
        {
            Dispatcher.UIThread.Post(RefreshWindowButtons);
        }
    }

    private void CheckRowRulesets()
    {
        var profile = _profileManager.CurrentProfile;
        var rowConfigs = profile.FloatingWindowRowRulesets;
        if (rowConfigs == null || rowConfigs.Count == 0)
        {
            if (_rulesetHiddenRows.Count > 0)
            {
                _rulesetHiddenRows.Clear();
                Dispatcher.UIThread.Post(RefreshWindowButtons);
            }
            return;
        }

        var rulesetService = IAppHost.TryGetService<IRulesetService>();
        if (rulesetService == null)
        {
            return;
        }

        var changed = false;
        for (int i = 0; i < rowConfigs.Count; i++)
        {
            var config = rowConfigs[i];
            var shouldHide = false;
            if (!config.IsVisible)
            {
                shouldHide = true;
            }
            else if (config.HideOnRule)
            {
                shouldHide = rulesetService.IsRulesetSatisfied(config.HidingRules);
            }

            var wasHidden = _rulesetHiddenRows.Contains(i);
            if (shouldHide != wasHidden)
            {
                if (shouldHide)
                {
                    _rulesetHiddenRows.Add(i);
                }
                else
                {
                    _rulesetHiddenRows.Remove(i);
                }
                changed = true;
            }
        }

        if (changed)
        {
            Dispatcher.UIThread.Post(RefreshWindowButtons);
        }
    }

    private void ApplyVisibility()
    {
        if (_isStopped) return;
        EnsureWindow();
        if (_window == null)
        {
            return;
        }

        var profile = _profileManager.CurrentProfile;
        var hasVisibleButtons = HasAnyVisibleButton();
        var shouldShow = _configHandler.Data.ShowFloatingWindow && hasVisibleButtons && !_rulesetHidingWindow;

        if (shouldShow)
        {
            if (!_window.IsVisible)
            {
                try
                {
                    _window.Show();
                }
                catch (InvalidOperationException)
                {
                    DiscardWindowState();
                    if (_isStopped) return;
                    EnsureWindow();
                    if (_window != null)
                    {
                        try { _window.Show(); }
                        catch (InvalidOperationException) { /* 放弃重建 */ }
                    }
                }
            }
        }
        else
        {
            if (_window != null && _window.IsVisible)
            {
                try
                {
                    _window.Hide();
                    StopLiquidGlassCapture();
                }
                catch (InvalidOperationException)
                {
                    DiscardWindowState();
                }
            }
        }

        UpdateLiquidGlassCaptureLoop();
    }

    private void DiscardWindowState()
    {
        _window = null;
        StopLiquidGlassCapture();
        _windowRoot = null;
        _stackPanel = null;
        _windowContainer = null;
        _liquidGlassBackdropClip = null;
        _liquidGlassSurface = null;
        _touchDragHandle = null;
        _windowBoundsClampQueued = false;
        _pointerPressed = false;
        _dragInitiated = false;
        _lastPressedArgs = null;
        _touchDragAllowed = false;
    }

    private void RefreshWindowButtons()
    {
        if (_stackPanel == null)
        {
            return;
        }

        var profile = _profileManager.CurrentProfile;
        var config = _configHandler.Data;
        var scale = Math.Clamp(config.FloatingWindowScale, 0.5, 2.0);
        var iconSize = Math.Clamp(config.FloatingWindowIconSize, 15, 50) * scale;
        var textSize = Math.Clamp(config.FloatingWindowTextSize, 8, 30) * scale;
        var isLightTheme = IsLightTheme();
        var contentForeground = isLightTheme ? Brushes.Black : Brushes.White;
        var useLiquidGlass = IsLiquidGlassRequested();
        var settings = config.FloatingWindowLiquidGlass;

        if (_lastButtonLayoutStyle != config.FloatingWindowAppearanceStyle ||
            double.IsNaN(_lastButtonLayoutScale) ||
            Math.Abs(_lastButtonLayoutScale - scale) > 0.0001)
        {
            _buttonWidthCache.Clear();
            _lastButtonLayoutStyle = config.FloatingWindowAppearanceStyle;
            _lastButtonLayoutScale = scale;
        }

        _stackPanel.Orientation = Orientation.Vertical;
        _stackPanel.Spacing = (useLiquidGlass ? 8 : 6) * scale;
        _stackPanel.Margin = new Thickness((useLiquidGlass ? 12 : 6) * scale);
        _stackPanel.HorizontalAlignment = HorizontalAlignment.Center;

        _stackPanel.Children.Clear();
        _touchDragHandle = null;

        int rowIndex = 0;
        foreach (var rowEntries in GetOrderedRows())
        {
            if (_rulesetHiddenRows.Contains(rowIndex))
            {
                rowIndex++;
                continue;
            }

            var rowPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = (useLiquidGlass ? 8 : 6) * scale,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            foreach (var entry in rowEntries)
            {
                var iconBlock = new FluentIcon
                {
                    Glyph = ConvertIcon(entry.Icon),
                    FontSize = iconSize,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = contentForeground
                };

                var nameBlock = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(entry.Name) ? "触发" : entry.Name,
                    FontSize = textSize,
                    FontWeight = useLiquidGlass ? FontWeight.SemiBold : FontWeight.Normal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = useLiquidGlass ? TextWrapping.NoWrap : TextWrapping.Wrap,
                    TextTrimming = useLiquidGlass ? TextTrimming.CharacterEllipsis : TextTrimming.None,
                    MaxWidth = 100 * scale,
                    Margin = useLiquidGlass ? default : new Thickness(0, 2 * scale, 0, 0),
                    Foreground = contentForeground
                };

                var contentPanel = new StackPanel
                {
                    Orientation = useLiquidGlass ? Orientation.Horizontal : Orientation.Vertical,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = useLiquidGlass ? 7 * scale : 2 * scale,
                    Children =
                    {
                        iconBlock,
                        nameBlock
                    }
                };

                var button = new Button
                {
                    Content = contentPanel,
                    MinWidth = useLiquidGlass ? 104 * scale : 54 * scale,
                    MinHeight = useLiquidGlass ? 48 * scale : 52 * scale,
                    Padding = useLiquidGlass
                        ? new Thickness(14 * scale, 4 * scale)
                        : new Thickness(6 * scale, 4 * scale),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = contentForeground,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center
                };

                if (useLiquidGlass)
                {
                    nameBlock.MaxWidth = 150 * scale;
                }

                if (entry.IsRevertStyleActive)
                {
                    button.Background = useLiquidGlass
                        ? Brushes.Transparent
                        : TryGetButtonPointerOverBrush() ??
                          new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));

                    if (_buttonWidthCache.TryGetValue(entry.ButtonId, out var cachedWidth) && cachedWidth > 0)
                    {
                        button.Width = cachedWidth;
                    }
                }
                else
                {
                    button.Width = double.NaN;
                }

                Control buttonHost = button;
                LiquidGlassInteractiveSurface? glassButton = null;
                if (useLiquidGlass)
                {
                    var reduceMotion = SystemTools.Views.SystemMotionPreferences.ShouldReduceMotion();
                    glassButton = new LiquidGlassInteractiveSurface
                    {
                        Child = button,
                        CornerRadius = new CornerRadius(999),
                        IsInteractive = true,
                        InteractiveHighlightEnabled = true,
                        InteractiveMaxScaleDip = reduceMotion
                            ? 0
                            : Math.Clamp(config.FloatingWindowGlassButtonScaleDip, 0, 12),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    ApplyLiquidGlassSettings(glassButton, settings, scale, config.FloatingWindowShadowEnabled);
                    glassButton.CornerRadius = new CornerRadius(999);
                    glassButton.ShadowRadius = Math.Min(glassButton.ShadowRadius, 14 * scale);
                    glassButton.ShadowOffset = new Vector(0, 2 * scale);
                    glassButton.ShadowOpacity = Math.Min(glassButton.ShadowOpacity, 0.55);
                    if (entry.IsRevertStyleActive)
                    {
                        glassButton.SurfaceColor = ParseGlassColor(
                            isLightTheme ? "#55FFFFFF" : "#553A3A3A",
                            Colors.Transparent);
                    }

                    buttonHost = glassButton;
                }

                if (!entry.IsRevertStyleActive)
                {
                    EventHandler? layoutUpdatedHandler = null;
                    layoutUpdatedHandler = (_, _) =>
                    {
                        var width = button.Bounds.Width;
                        if (width > 0)
                        {
                            _buttonWidthCache[entry.ButtonId] = width;
                            buttonHost.LayoutUpdated -= layoutUpdatedHandler;
                        }
                    };
                    buttonHost.LayoutUpdated += layoutUpdatedHandler;
                }

                button.PointerPressed += (_, e) =>
                {
                    if (!entry.IsRevertStyleActive || !entry.IsRevertEnabled)
                    {
                        return;
                    }

                    if (e.GetCurrentPoint(button).Properties.IsRightButtonPressed)
                    {
                        entry.CancelIsOnAction();
                        e.Handled = true;
                    }
                };

                button.Click += (_, _) => entry.TriggerAction();
                rowPanel.Children.Add(buttonHost);
            }

            _stackPanel.Children.Add(rowPanel);

            rowIndex++;
        }

        // 仅在"至少有一个可见按钮"时才显示拖拽把手，避免孤零零一个把手
        var hasVisibleButtons = _stackPanel.Children.Count > 0;
        if (hasVisibleButtons)
        {
            _touchDragHandle = CreateTouchDragHandle(scale, contentForeground);
            UpdateDragHandleVisibility();
            _stackPanel.Children.Insert(0, _touchDragHandle);
        }
    }

    /// <summary>
    /// 判断是否至少有 1 个按钮在"经过规则集过滤后"是可见的。
    /// 用于避免悬浮窗在没有任何可见按钮时（被规则集全部隐藏）仍然显示。
    /// </summary>
    private bool HasAnyVisibleButton()
    {
        if (_entries.Count == 0)
        {
            return false;
        }

        var profile = _profileManager.CurrentProfile;
        var rowConfigs = profile.FloatingWindowRowRulesets;
        var hiddenRowSet = new HashSet<int>();

        if (rowConfigs != null)
        {
            for (int i = 0; i < rowConfigs.Count; i++)
            {
                var cfg = rowConfigs[i];
                var shouldHide = !cfg.IsVisible
                    || (cfg.HideOnRule && cfg.HidingRules != null
                        && IAppHost.TryGetService<IRulesetService>() is { } rs
                        && rs.IsRulesetSatisfied(cfg.HidingRules));
                if (shouldHide)
                {
                    hiddenRowSet.Add(i);
                }
            }
        }

        int rowIndex = 0;
        foreach (var row in GetConfiguredButtonRowsWithFallback(profile))
        {
            if (!hiddenRowSet.Contains(rowIndex))
            {
                foreach (var id in row)
                {
                    if (_rulesetHiddenButtons.Contains(id))
                    {
                        continue;
                    }
                    foreach (var entry in _entries.Values)
                    {
                        if (string.Equals(entry.ButtonId, id, StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }
                }
            }
            rowIndex++;
        }

        return false;
    }

    private List<List<FloatingWindowEntry>> GetOrderedRows()
    {
        var profile = _profileManager.CurrentProfile;
        var validButtonIds = _entries.Values.Select(x => x.ButtonId).ToHashSet();

        // 清理不存在的按钮ID
        if (profile.PruneInvalidButtonIds(validButtonIds))
        {
            _profileManager.SaveProfile();
        }

        var values = _entries.Values
            .Where(x => !_rulesetHiddenButtons.Contains(x.ButtonId))
            .GroupBy(x => x.ButtonId)
            .ToDictionary(g => g.Key, g => g.First());

        var rows = new List<List<FloatingWindowEntry>>();

        foreach (var row in GetConfiguredButtonRowsWithFallback(profile))
        {
            var items = new List<FloatingWindowEntry>();
            foreach (var id in row)
            {
                if (values.TryGetValue(id, out var entry))
                {
                    items.Add(entry);
                }
            }
            if (items.Count > 0)
            {
                rows.Add(items);
            }
        }

        return rows;
    }


    private List<List<string>> GetConfiguredButtonRowsWithFallback(FloatingWindowProfile profile)
    {
        var validButtonIds = _entries.Values.Select(x => x.ButtonId).Distinct().ToList();
        var validSet = validButtonIds.ToHashSet();
        var rows = (profile.FloatingWindowButtonRows ?? [])
            .Select(row => row.Where(validSet.Contains).Distinct().ToList())
            .Where(row => row.Count > 0)
            .ToList();

        var configuredIds = rows.SelectMany(row => row).ToHashSet();
        var missingIds = validButtonIds
            .Where(id => !configuredIds.Contains(id))
            .Where(id => !profile.FloatingWindowButtonRulesets.ContainsKey(id))
            .ToList();

        if (missingIds.Count == 0)
        {
            return rows;
        }

        if (rows.Count == 0)
        {
            rows.Add(missingIds);
        }
        else
        {
            rows[0].AddRange(missingIds);
        }

        return rows;
    }

    private void PruneButtonWidthCache()
    {
        if (_buttonWidthCache.Count == 0)
        {
            return;
        }

        var validIds = _entries.Values.Select(x => x.ButtonId).ToHashSet();
        var staleIds = _buttonWidthCache.Keys.Where(id => !validIds.Contains(id)).ToList();
        foreach (var id in staleIds)
        {
            _buttonWidthCache.Remove(id);
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_window == null)
        {
            return;
        }

        UpdateInputMode(e.Pointer.Type);

        if (_isTouchDeviceDetected)
        {
            if (!IsTouchLikePointer(e) || !IsEventFromTouchDragHandle(e.Source))
            {
                _touchDragAllowed = false;
                return;
            }

            _touchDragAllowed = true;
            _touchDragStartScreenPoint = _window.PointToScreen(e.GetPosition(_window));
            _touchDragStartWindowPosition = _window.Position;
            e.Handled = true;
            return;
        }

        if (!e.GetCurrentPoint(_window).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // Glass buttons own their press deformation and click. Drag only from the material's
        // empty area or the explicit handle so a click never moves the window.
        if (IsEventFromGlassButton(e.Source))
        {
            return;
        }

        _pointerPressed = true;
        _dragInitiated = false;
        _pointerDownPoint = e.GetPosition(_window);
        _lastPressedArgs = e;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_window == null)
        {
            return;
        }

        UpdateInputMode(e.Pointer.Type);

        if (_isTouchDeviceDetected)
        {
            if (!IsTouchLikePointer(e) || !_touchDragAllowed)
            {
                return;
            }

            var screenPoint = _window.PointToScreen(e.GetPosition(_window));
            var deltaX = screenPoint.X - _touchDragStartScreenPoint.X;
            var deltaY = screenPoint.Y - _touchDragStartScreenPoint.Y;
            var target = new PixelPoint(_touchDragStartWindowPosition.X + deltaX,
                _touchDragStartWindowPosition.Y + deltaY);
            _window.Position = ClampToVisibleScreen(target);
            e.Handled = true;
            return;
        }

        if (!_pointerPressed || _dragInitiated)
        {
            return;
        }

        var point = e.GetPosition(_window);
        var dx = point.X - _pointerDownPoint.X;
        var dy = point.Y - _pointerDownPoint.Y;

        if (Math.Abs(dx) + Math.Abs(dy) < 4)
        {
            return;
        }

        _dragInitiated = true;
        if (_lastPressedArgs != null)
        {
            _window.BeginMoveDrag(_lastPressedArgs);
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_window == null)
        {
            return;
        }

        if (!_isTouchDeviceDetected && IsEventFromGlassButton(e.Source))
        {
            _pointerPressed = false;
            _dragInitiated = false;
            _lastPressedArgs = null;
            return;
        }

        UpdateInputMode(e.Pointer.Type);

        if (_isTouchDeviceDetected)
        {
            if (!IsTouchLikePointer(e))
            {
                return;
            }

            var wasTouchDragging = _touchDragAllowed;
            _touchDragAllowed = false;
            if (!wasTouchDragging)
            {
                return;
            }

            var touchClamped = ClampToVisibleScreen(_window.Position);
            _window.Position = touchClamped;
            SavePosition(touchClamped);
            e.Handled = true;
            return;
        }

        _pointerPressed = false;
        _dragInitiated = false;
        _lastPressedArgs = null;

        var clamped = ClampToVisibleScreen(_window.Position);
        _window.Position = clamped;
        SavePosition(clamped);
    }

    private Border CreateTouchDragHandle(double scale, IBrush foreground)
    {
        var handle = new Border
        {
            Background = IsLiquidGlassRequested()
                ? new SolidColorBrush(Color.FromArgb(36, 255, 255, 255))
                : Brushes.Transparent,
            CornerRadius = new CornerRadius(999),
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(13 * scale, 5 * scale),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 3 * scale,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    CreateDragHandleDot(scale, foreground),
                    CreateDragHandleDot(scale, foreground),
                    CreateDragHandleDot(scale, foreground)
                }
            }
        };

        return handle;
    }

    private static Border CreateDragHandleDot(double scale, IBrush foreground)
    {
        return new Border
        {
            Width = 3 * scale,
            Height = 3 * scale,
            CornerRadius = new CornerRadius(999),
            Background = foreground,
            Opacity = 0.62
        };
    }

    private bool IsEventFromTouchDragHandle(object? source)
    {
        if (_touchDragHandle == null || source is not Visual visual)
        {
            return false;
        }

        var current = visual;
        while (current != null)
        {
            if (ReferenceEquals(current, _touchDragHandle))
            {
                return true;
            }

            current = current.GetVisualParent();
        }

        return false;
    }

    private static bool IsEventFromGlassButton(object? source)
    {
        if (source is not Visual visual)
        {
            return false;
        }

        var current = visual;
        while (current != null)
        {
            if (current is LiquidGlassInteractiveSurface)
            {
                return true;
            }

            current = current.GetVisualParent();
        }

        return false;
    }


    private bool IsTouchLikePointer(PointerEventArgs e)
    {
        return e.Pointer.Type == PointerType.Touch
               || (e.Pointer.Type == PointerType.Mouse && IsRecentTouchGeneratedMouseEvent());
    }

    private bool IsRecentTouchGeneratedMouseEvent()
    {
        return DateTime.UtcNow - _lastTouchGeneratedMouseEventAt <= TouchLikeMouseGracePeriod;
    }

    private void UpdateInputMode(PointerType pointerType)
    {
        if (pointerType == PointerType.Touch)
        {
            SetTouchInputMode(true);
            return;
        }

        if (pointerType == PointerType.Mouse)
        {
            if (IsRecentTouchGeneratedMouseEvent())
            {
                SetTouchInputMode(true);
                return;
            }

            SetTouchInputMode(false);
            return;
        }

        if (pointerType == PointerType.Pen)
        {
            SetTouchInputMode(false);
        }
    }

    private void SetTouchInputMode(bool isTouch)
    {
        if (_isTouchDeviceDetected == isTouch)
        {
            return;
        }

        _isTouchDeviceDetected = isTouch;
        _pointerPressed = false;
        _dragInitiated = false;
        _lastPressedArgs = null;
        _touchDragAllowed = false;
        Dispatcher.UIThread.Post(UpdateDragHandleVisibility);
    }

    private void UpdateDragHandleVisibility()
    {
        if (_touchDragHandle == null)
        {
            return;
        }

        _touchDragHandle.IsVisible = _isTouchDeviceDetected ||
                                     _configHandler.Data.FloatingWindowDragHandleAlwaysVisible;
    }

    private void EnsureGlobalInputHooks()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            return;
        }

        _lowLevelMouseProc ??= OnLowLevelMouse;
        _mouseHook = SetWindowsHookEx(WhMouseLl, _lowLevelMouseProc, IntPtr.Zero, 0);
    }

    private void RemoveGlobalInputHooks()
    {
        if (_mouseHook == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_mouseHook);
        _mouseHook = IntPtr.Zero;
    }

    private IntPtr OnLowLevelMouse(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || lParam == IntPtr.Zero)
        {
            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        var message = unchecked((uint)wParam.ToInt64());
        if (message != WmMouseMove && message != WmLButtonDown && message != WmRButtonDown)
        {
            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
        var extra = unchecked((ulong)info.dwExtraInfo.ToInt64());
        var isTouchGenerated = (extra & MiWpSignatureMask) == MiWpSignature;

        if (isTouchGenerated)
        {
            _lastTouchGeneratedMouseEventAt = DateTime.UtcNow;
            SetTouchInputMode(true);
        }
        else if (message == WmLButtonDown || message == WmRButtonDown)
        {
            SetTouchInputMode(false);
        }

        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private PixelRect GetWindowRect(PixelPoint position)
    {
        if (_window == null)
        {
            return new PixelRect(position.X, position.Y, 0, 0);
        }

        var size = GetWindowPixelSize();
        return new PixelRect(position.X, position.Y, size.Width, size.Height);
    }

    private bool IsWindowInsideAnyScreen(PixelRect rect)
    {
        if (_window?.Screens?.All is not { } screens || screens.Count == 0)
        {
            return true;
        }

        return screens.Any(screen => screen.WorkingArea.Intersects(rect));
    }

    private PixelPoint GetCenteredPositionOnPrimaryScreen()
    {
        if (_window?.Screens?.Primary is not { } primary || _window == null)
        {
            return _window?.Position ?? new PixelPoint(0, 0);
        }

        var area = primary.WorkingArea;
        var size = GetWindowPixelSize();
        var width = size.Width;
        var height = size.Height;

        var x = area.X + (area.Width - width) / 2;
        var y = area.Y + (area.Height - height) / 2;
        return new PixelPoint(x, y);
    }

    private PixelPoint ClampToVisibleScreen(PixelPoint position)
    {
        if (_window == null)
        {
            return position;
        }

        var screens = _window.Screens?.All;
        if (screens == null || screens.Count == 0)
        {
            return position;
        }

        var screen = screens.FirstOrDefault(s => s.WorkingArea.Contains(position))
                     ?? _window.Screens?.Primary
                     ?? screens[0];

        var area = screen.WorkingArea;
        var size = GetWindowPixelSize();
        var width = size.Width;
        var height = size.Height;

        var minX = area.X;
        var minY = area.Y;
        var maxX = area.X + Math.Max(0, area.Width - width);
        var maxY = area.Y + Math.Max(0, area.Height - height);

        return new PixelPoint(Math.Clamp(position.X, minX, maxX), Math.Clamp(position.Y, minY, maxY));
    }

    private PixelSize GetWindowPixelSize()
    {
        if (_window == null)
        {
            return new PixelSize(1, 1);
        }

        var scaling = Math.Max(0.1, _window.RenderScaling);
        var dipSize = _window.ClientSize.Width > 0 && _window.ClientSize.Height > 0
            ? _window.ClientSize
            : new Size(_window.Bounds.Width, _window.Bounds.Height);
        return new PixelSize(
            Math.Max(1, (int)Math.Ceiling(dipSize.Width * scaling)),
            Math.Max(1, (int)Math.Ceiling(dipSize.Height * scaling)));
    }

    private void EnsureWindowPositionVisibleOnStartup()
    {
        if (_window == null)
        {
            return;
        }

        var configured = new PixelPoint(_configHandler.Data.FloatingWindowPositionX, _configHandler.Data.FloatingWindowPositionY);
        var rect = GetWindowRect(configured);
        var target = IsWindowInsideAnyScreen(rect) ? ClampToVisibleScreen(configured) : GetCenteredPositionOnPrimaryScreen();

        _window.Position = target;
        SavePosition(target, forceSave: configured != target);
    }

    private void SavePosition(PixelPoint position, bool forceSave = false)
    {
        var changed = false;

        if (_configHandler.Data.FloatingWindowPositionX != position.X)
        {
            _configHandler.Data.FloatingWindowPositionX = position.X;
            changed = true;
        }

        if (_configHandler.Data.FloatingWindowPositionY != position.Y)
        {
            _configHandler.Data.FloatingWindowPositionY = position.Y;
            changed = true;
        }

        if (forceSave || changed)
        {
            _configHandler.Save();
        }
    }

    private void EnsureLayerRecheckHooks()
    {
        if (_winEventProc == null)
        {
            _winEventProc = OnWinEvent;
        }

        LayerRecheck50MsTimer.Tick -= OnLayerRecheck50MsTimerTick;
        LayerRecheck50MsTimer.Tick += OnLayerRecheck50MsTimerTick;
        LayerRecheck1MsTimer.Tick -= OnLayerRecheck1MsTimerTick;
        LayerRecheck1MsTimer.Tick += OnLayerRecheck1MsTimerTick;

        // 规则集巡检由 ILessonsService.PostMainTimerTicked 驱动
        _lessonsService ??= IAppHost.TryGetService<ILessonsService>();
        if (_lessonsService != null)
        {
            _lessonsService.PostMainTimerTicked -= OnPostMainTimerTicked;
            _lessonsService.PostMainTimerTicked += OnPostMainTimerTicked;
        }
    }

    private void RemoveLayerRecheckHooks()
    {
        if (_foregroundHook != IntPtr.Zero)
        {
            UnhookWinEvent(_foregroundHook);
            _foregroundHook = default;
        }

        if (_reorderHook != IntPtr.Zero)
        {
            UnhookWinEvent(_reorderHook);
            _reorderHook = default;
        }

        LayerRecheck50MsTimer.Tick -= OnLayerRecheck50MsTimerTick;
        LayerRecheck1MsTimer.Tick -= OnLayerRecheck1MsTimerTick;

        if (_lessonsService != null)
        {
            _lessonsService.PostMainTimerTicked -= OnPostMainTimerTicked;
        }
    }

    private void RefreshLayerRecheckMode()
    {
        var mode = _configHandler.Data.FloatingWindowLayerRecheckMode;
        var useReorderHook = mode == 0;
        var useForegroundHook = mode == 1;

        if (useForegroundHook)
        {
            EnsureForegroundHook();
        }
        else
        {
            RemoveForegroundHook();
        }

        if (useReorderHook)
        {
            EnsureReorderHook();
        }
        else
        {
            RemoveReorderHook();
        }

        LayerRecheck50MsTimer.IsEnabled = mode == 2;
        LayerRecheck1MsTimer.IsEnabled = mode == 3;
    }

    private void EnsureForegroundHook()
    {
        if (_foregroundHook != IntPtr.Zero || _winEventProc == null)
        {
            return;
        }

        _foregroundHook = SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            IntPtr.Zero,
            _winEventProc,
            0,
            0,
            WinEventOutOfContext | WinEventSkipOwnProcess);
    }

    private void EnsureReorderHook()
    {
        if (_reorderHook != IntPtr.Zero || _winEventProc == null)
        {
            return;
        }

        _reorderHook = SetWinEventHook(
            EventObjectReorder,
            EventObjectReorder,
            IntPtr.Zero,
            _winEventProc,
            0,
            0,
            WinEventOutOfContext | WinEventSkipOwnProcess);
    }

    private void RemoveForegroundHook()
    {
        if (_foregroundHook == IntPtr.Zero)
        {
            return;
        }

        UnhookWinEvent(_foregroundHook);
        _foregroundHook = default;
    }

    private void RemoveReorderHook()
    {
        if (_reorderHook == IntPtr.Zero)
        {
            return;
        }

        UnhookWinEvent(_reorderHook);
        _reorderHook = default;
    }

    private void OnPostMainTimerTicked(object? sender, EventArgs e)
    {
        // 模式 2/3 改回 DispatcherTimer Tick 事件触发，本回调只负责规则集巡检
        CheckFloatingWindowRuleset();
        CheckButtonRulesets();
        CheckRowRulesets();
        // 兜底 ApplyVisibility：避免所有按钮都被隐藏但窗口仍显示
        ApplyVisibility();
    }

    private void OnLayerRecheck50MsTimerTick(object? sender, EventArgs e)
    {
        if (_configHandler.Data.FloatingWindowLayerRecheckMode == 2)
        {
            RecheckWindowLayer();
        }
    }

    private void OnLayerRecheck1MsTimerTick(object? sender, EventArgs e)
    {
        if (_configHandler.Data.FloatingWindowLayerRecheckMode == 3)
        {
            RecheckWindowLayer();
        }
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint @event, IntPtr hwnd, int idObject, int idChild, uint idEventThread,
        uint dwmsEventTime)
    {
        if (_window == null)
        {
            return;
        }

        var mode = _configHandler.Data.FloatingWindowLayerRecheckMode;
        var shouldRecheck = (@event == EventObjectReorder && mode == 0) ||
                            (@event == EventSystemForeground && mode == 1);
        if (!shouldRecheck)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            RecheckWindowLayer();
        });
    }

    private void RecheckWindowLayer()
    {
        if (_window == null)
        {
            return;
        }

        var handle = _window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var flags = SET_WINDOW_POS_FLAGS.SWP_NOMOVE |
                    SET_WINDOW_POS_FLAGS.SWP_NOSIZE |
                    SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE |
                    SET_WINDOW_POS_FLAGS.SWP_NOSENDCHANGING;
        var hwnd = new HWND(handle);

        if (_configHandler.Data.FloatingWindowLayer == 0)
        {
            _window.Topmost = false;
            PInvoke.SetWindowPos(hwnd, HwndBottom, 0, 0, 0, 0, flags);
            return;
        }

        _window.Topmost = true;
        PInvoke.SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, flags);
    }

    public void ToggleWindowLayer()
    {
        SetWindowLayer(_configHandler.Data.FloatingWindowLayer == 1 ? 0 : 1);
    }

    public void SetWindowLayer(int layer)
    {
        _configHandler.Data.FloatingWindowLayer = layer == 1 ? 1 : 0;
        _configHandler.Save();
        Dispatcher.UIThread.Post(() =>
        {
            if (_window != null)
            {
                _window.Topmost = _configHandler.Data.FloatingWindowLayer == 1;
            }
            RecheckWindowLayer();
            RefreshLayerRecheckMode();
        });
    }

    public void ToggleWindowProfile()
    {
        var names = _profileManager.GetProfileNames();
        if (names.Count <= 1)
        {
            return;
        }

        var currentName = _profileManager.CurrentProfileName;
        var currentIndex = -1;
        for (int i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], currentName, StringComparison.OrdinalIgnoreCase))
            {
                currentIndex = i;
                break;
            }
        }
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        var newIndex = (currentIndex + 1) % names.Count;
        var newName = names[newIndex];
        SwitchToProfile(newName);
    }

    public void SwitchToProfile(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return;
        }

        var names = _profileManager.GetProfileNames();
        if (!names.Contains(profileName))
        {
            return;
        }

        // 只在当前方案文件还存在时才保存，避免刚被删除的方案被重新写回磁盘
        if (_profileManager.ProfileFileExists(_profileManager.CurrentProfileName))
        {
            _profileManager.SaveProfile();
        }
        _profileManager.LoadProfile(profileName);
        _configHandler.Data.CurrentFloatingWindowProfile = profileName;
        _configHandler.Save();

        Dispatcher.UIThread.Post(() =>
        {
            RefreshWindowButtons();
            ApplyVisibility();
            RecheckWindowLayer();
            RefreshLayerRecheckMode();
        });
    }

    private static IBrush? TryParseColor(string colorString)
    {
        try
        {
            return new SolidColorBrush(Color.Parse(colorString));
        }
        catch
        {
            return null;
        }
    }

    public static string ConvertIcon(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "\uEA37";
        var v = raw.Trim();
        if (v.StartsWith("/u", StringComparison.OrdinalIgnoreCase) || v.StartsWith("\\u", StringComparison.OrdinalIgnoreCase))
        {
            var hex = v[2..];
            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var code))
            {
                return char.ConvertFromUtf32(code);
            }
        }

        return v;
    }

    private static IBrush? TryGetButtonPointerOverBrush()
    {
        if (Application.Current == null)
        {
            return null;
        }

        if (Application.Current.TryGetResource("SubtleFillColorSecondaryBrush", null, out var subtle) &&
            subtle is IBrush subtleBrush)
        {
            return subtleBrush;
        }

        if (Application.Current.TryGetResource("ControlFillColorSecondaryBrush", null, out var control) &&
            control is IBrush controlBrush)
        {
            return controlBrush;
        }

        return null;
    }
}

public record FloatingWindowEntry(
    string ButtonId,
    string Icon,
    string Name,
    bool IsRevertStyleActive,
    bool IsRevertEnabled,
    string LayoutName,
    Action TriggerAction,
    Action CancelIsOnAction);
