using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClassIsland.Core.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using SystemTools.ConfigHandlers;
using SystemTools.Triggers;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

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
    private static readonly TimeSpan TouchLikeMouseGracePeriod = TimeSpan.FromMilliseconds(250);

    private readonly MainConfigHandler _configHandler;
    private readonly Dictionary<FloatingWindowTrigger, FloatingWindowEntry> _entries = new();
    private Window? _window;
    private StackPanel? _stackPanel;
    private Border? _windowContainer;

    private bool _pointerPressed;
    private bool _dragInitiated;
    private Point _pointerDownPoint;
    private PointerPressedEventArgs? _lastPressedArgs;
    private bool _isThemeSubscribed;
    private readonly Dictionary<string, double> _buttonWidthCache = new();
    private bool _allowWindowClose;
    private bool _restoringFromMinimized;
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

    public FloatingWindowService(MainConfigHandler configHandler)
    {
        _configHandler = configHandler;
    }

    public IReadOnlyList<FloatingWindowEntry> Entries => _entries.Values.ToList();

    public void Start()
    {
        Dispatcher.UIThread.Post(() =>
        {
            EnsureWindow();
            EnsureLayerRecheckHooks();
            EnsureGlobalInputHooks();
            SubscribeThemeChanged();
            ApplyVisibility();
            RefreshLayerRecheckMode();
            RecheckWindowLayer();
            RefreshWindowButtons();
        });
    }

    public void Stop()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_window != null)
            {
                _allowWindowClose = true;
                _window.Close();
                _window = null;
            }

            LayerRecheck50MsTimer.Stop();
            LayerRecheck1MsTimer.Stop();
            RemoveLayerRecheckHooks();
            RemoveGlobalInputHooks();
            UnsubscribeThemeChanged();
        });
    }

    public void RegisterTrigger(FloatingWindowTrigger trigger)
    {
        _entries[trigger] = new FloatingWindowEntry(
            trigger.GetButtonId(),
            trigger.GetIcon(),
            trigger.GetButtonName(),
            trigger.ShouldUseRevertStyle(),
            trigger.IsRevertEnabled(),
            trigger.GetLayoutButtonName(),
            trigger.TriggerFromFloatingWindow,
            trigger.CancelIsOnState);

        PruneButtonWidthCache();
        NotifyEntriesChanged();
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
        Dispatcher.UIThread.Post(() =>
        {
            ApplyVisibility();
            RefreshLayerRecheckMode();
            RecheckWindowLayer();
            RefreshWindowButtons();
        });
    }

    private void NotifyEntriesChanged()
    {
        EntriesChanged?.Invoke(this, EventArgs.Empty);
        Dispatcher.UIThread.Post(() =>
        {
            ApplyVisibility();
            RecheckWindowLayer();
            RefreshWindowButtons();
        });
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
            && _configHandler.Data.FloatingWindowTheme == 0)
        {
            Dispatcher.UIThread.Post(RefreshWindowButtons);
        }
    }

    private ThemeVariant ResolveWindowThemeVariant()
    {
        return _configHandler.Data.FloatingWindowTheme switch
        {
            1 => ThemeVariant.Light,
            2 => ThemeVariant.Dark,
            _ => _window?.ActualThemeVariant ?? Application.Current?.ActualThemeVariant ?? ThemeVariant.Dark
        };
    }

    private bool IsLightTheme()
    {
        return ResolveWindowThemeVariant() == ThemeVariant.Light;
    }

    private void EnsureWindow()
    {
        if (_window != null)
        {
            return;
        }

        _allowWindowClose = false;
        _stackPanel = new StackPanel { Margin = new Thickness(6), Spacing = 6 };
        _window = new Window
        {
            Width = 1,
            Height = 1,
            ShowActivated = false,
            Topmost = _configHandler.Data.FloatingWindowLayer == 1,
            SystemDecorations = SystemDecorations.None,
            Background = Brushes.Transparent,
            CanResize = false,
            ShowInTaskbar = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = _windowContainer = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#CC1F1F1F")),
                CornerRadius = new CornerRadius(8),
                Child = _stackPanel
            }
        };

        _window.Loaded += OnWindowLoaded;
        _window.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, true);
        _window.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, true);
        _window.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, true);
        _window.Closing += (_, e) =>
        {
            if (!_allowWindowClose)
            {
                e.Cancel = true;
                if (_window is { IsVisible: false })
                {
                    _window.Show();
                }
            }
        };
        _window.PropertyChanged += OnWindowPropertyChanged;

        _window.Show();
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
        if (_window == null || _restoringFromMinimized)
        {
            return;
        }

        _restoringFromMinimized = true;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (_window == null)
                {
                    return;
                }

                if (!_window.IsVisible)
                {
                    _window.Show();
                }

                _window.WindowState = WindowState.Normal;
            }
            finally
            {
                _restoringFromMinimized = false;
            }
        }, DispatcherPriority.Background);
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        _window!.TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        EnsureWindowPositionVisibleOnStartup();
        RecheckWindowLayer();
    }

    private void ApplyVisibility()
    {
        EnsureWindow();
        if (_window == null)
        {
            return;
        }

        if (_configHandler.Data.ShowFloatingWindow && _entries.Count > 0)
        {
            if (!_window.IsVisible)
            {
                _window.Show();
            }
        }
        else
        {
            _window.Hide();
        }
    }

    private void RefreshWindowButtons()
    {
        if (_stackPanel == null)
        {
            return;
        }

        var scale = Math.Clamp(_configHandler.Data.FloatingWindowScale, 0.5, 2.0);
        var iconSize = Math.Clamp(_configHandler.Data.FloatingWindowIconSize, 15, 50) * scale;
        var textSize = Math.Clamp(_configHandler.Data.FloatingWindowTextSize, 8, 30) * scale;
        var opacity = Math.Clamp(_configHandler.Data.FloatingWindowOpacity, 10, 100);
        var alpha = (byte)Math.Round(255 * (opacity / 100.0));
        var isLightTheme = IsLightTheme();
        var windowBackground = isLightTheme
            ? new SolidColorBrush(Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF))
            : new SolidColorBrush(Color.FromArgb(alpha, 0x1F, 0x1F, 0x1F));
        var contentForeground = isLightTheme ? Brushes.Black : Brushes.White;

        if (_windowContainer != null)
        {
            _windowContainer.Background = windowBackground;
            _windowContainer.BoxShadow = _configHandler.Data.FloatingWindowShadowEnabled
                ? new BoxShadows(new BoxShadow
                {
                    OffsetX = 0,
                    OffsetY = 6 * scale,
                    Blur = 18 * scale,
                    Spread = 0,
                    Color = isLightTheme ? Color.Parse("#28000000") : Color.Parse("#60000000")
                })
                : default;
        }

        _stackPanel.Orientation = Orientation.Vertical;
        _stackPanel.Spacing = 6 * scale;
        _stackPanel.Margin = new Thickness(6 * scale);
        _stackPanel.HorizontalAlignment = HorizontalAlignment.Center;

        _stackPanel.Children.Clear();

        if (_isTouchDeviceDetected)
        {
            _touchDragHandle = CreateTouchDragHandle(scale, contentForeground);
            _stackPanel.Children.Add(_touchDragHandle);
        }
        else
        {
            _touchDragHandle = null;
        }

        foreach (var rowEntries in GetOrderedRows())
        {
            var rowPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6 * scale,
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
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 100 * scale,
                    Margin = new Thickness(0, 2 * scale, 0, 0),
                    Foreground = contentForeground
                };

                var contentPanel = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 2 * scale,
                    Children =
                    {
                        iconBlock,
                        nameBlock
                    }
                };

                var button = new Button
                {
                    Content = contentPanel,
                    MinWidth = 54 * scale,
                    MinHeight = 52 * scale,
                    Padding = new Thickness(6 * scale, 4 * scale),
                    Background = Brushes.Transparent,
                    Foreground = contentForeground,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center
                };

                if (entry.IsRevertStyleActive)
                {
                    button.Background = TryGetButtonPointerOverBrush() ??
                                        new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));

                    if (_buttonWidthCache.TryGetValue(entry.ButtonId, out var cachedWidth) && cachedWidth > 0)
                    {
                        button.Width = cachedWidth;
                    }
                }
                else
                {
                    // 保持自动布局，允许文本变更/缩放后重新测量自然宽度。
                    button.Width = double.NaN;
                }

                button.LayoutUpdated += (_, _) =>
                {
                    if (entry.IsRevertStyleActive)
                    {
                        return;
                    }

                    var width = button.Bounds.Width;
                    if (width > 0)
                    {
                        _buttonWidthCache[entry.ButtonId] = width;
                    }
                };

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
                rowPanel.Children.Add(button);
            }

            if (rowPanel.Children.Count > 0)
            {
                _stackPanel.Children.Add(rowPanel);
            }
        }
    }

    private List<List<FloatingWindowEntry>> GetOrderedRows()
    {
        var values = _entries.Values.ToDictionary(x => x.ButtonId, x => x);
        var order = _configHandler.Data.FloatingWindowButtonOrder ?? [];

        var orderedIds = values.Keys
            .OrderBy(id =>
            {
                var index = order.IndexOf(id);
                return index < 0 ? int.MaxValue : index;
            })
            .ThenBy(id => id)
            .ToList();

        var used = new HashSet<string>();
        var rows = new List<List<FloatingWindowEntry>>();

        foreach (var row in _configHandler.Data.FloatingWindowButtonRows ?? [])
        {
            var items = row
                .Where(id => values.ContainsKey(id) && used.Add(id))
                .Select(id => values[id])
                .ToList();
            if (items.Count > 0)
            {
                rows.Add(items);
            }
        }

        var missing = orderedIds
            .Where(id => !used.Contains(id))
            .Select(id => values[id])
            .ToList();

        if (rows.Count == 0)
        {
            rows.Add(missing);
        }
        else
        {
            rows[0].AddRange(missing);
        }

        if (rows.Count == 0)
        {
            rows.Add([]);
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
        return new Border
        {
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(12 * scale, 3 * scale),
            Child = new TextBlock
            {
                Text = ": : :",
                FontSize = 12 * scale,
                Foreground = foreground,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            }
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
        Dispatcher.UIThread.Post(RefreshWindowButtons);
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
            // 仅在明确的鼠标点击操作时切回鼠标模式，避免触屏后被背景鼠标移动事件自动恢复。
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

        var width = Math.Max(1, (int)Math.Ceiling(_window.Bounds.Width));
        var height = Math.Max(1, (int)Math.Ceiling(_window.Bounds.Height));
        return new PixelRect(position.X, position.Y, width, height);
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
        var width = Math.Max(1, (int)Math.Ceiling(_window.Bounds.Width));
        var height = Math.Max(1, (int)Math.Ceiling(_window.Bounds.Height));

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
        var width = Math.Max(1, (int)Math.Ceiling(_window.Bounds.Width));
        var height = Math.Max(1, (int)Math.Ceiling(_window.Bounds.Height));

        var minX = area.X;
        var minY = area.Y;
        var maxX = area.X + Math.Max(0, area.Width - width);
        var maxY = area.Y + Math.Max(0, area.Height - height);

        return new PixelPoint(Math.Clamp(position.X, minX, maxX), Math.Clamp(position.Y, minY, maxY));
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

    public static string ConvertIcon(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "?";
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
