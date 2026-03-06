using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using SystemTools.ConfigHandlers;
using SystemTools.Triggers;

namespace SystemTools.Services;

public class FloatingWindowService
{
    private readonly MainConfigHandler _configHandler;
    private readonly Dictionary<FloatingWindowTrigger, FloatingWindowEntry> _entries = new();
    private Window? _window;
    private StackPanel? _stackPanel;

    private bool _pointerPressed;
    private bool _dragInitiated;
    private Point _pointerDownPoint;
    private PointerPressedEventArgs? _lastPressedArgs;

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
            ApplyVisibility();
            RefreshWindowButtons();
        });
    }

    public void Stop()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_window != null)
            {
                _window.Close();
                _window = null;
            }
        });
    }

    public void RegisterTrigger(FloatingWindowTrigger trigger)
    {
        _entries[trigger] = new FloatingWindowEntry(
            trigger.GetButtonId(),
            trigger.GetIcon(),
            trigger.GetButtonName(),
            trigger.TriggerFromFloatingWindow);

        NotifyEntriesChanged();
    }

    public void UnregisterTrigger(FloatingWindowTrigger trigger)
    {
        if (_entries.Remove(trigger))
        {
            NotifyEntriesChanged();
        }
    }

    public void UpdateWindowState()
    {
        Dispatcher.UIThread.Post(() =>
        {
            ApplyVisibility();
            RefreshWindowButtons();
        });
    }

    private void NotifyEntriesChanged()
    {
        EntriesChanged?.Invoke(this, EventArgs.Empty);
        Dispatcher.UIThread.Post(() =>
        {
            ApplyVisibility();
            RefreshWindowButtons();
        });
    }

    private void EnsureWindow()
    {
        if (_window != null)
        {
            return;
        }

        _stackPanel = new StackPanel { Margin = new Thickness(6), Spacing = 6 };
        _window = new Window
        {
            Width = 1,
            Height = 1,
            ShowActivated = false,
            Topmost = true,
            SystemDecorations = SystemDecorations.None,
            Background = Brushes.Transparent,
            CanResize = false,
            ShowInTaskbar = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = new Border
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
        _window.PositionChanged += (_, _) => SavePosition();
        _window.Closing += (_, e) =>
        {
            if (_configHandler.Data.ShowFloatingWindow)
            {
                e.Cancel = true;
                _window?.Hide();
            }
        };

        _window.Show();
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        _window!.Position = new PixelPoint(_configHandler.Data.FloatingWindowPositionX,
            _configHandler.Data.FloatingWindowPositionY);
        _window.TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
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

        _stackPanel.Orientation = _configHandler.Data.FloatingWindowHorizontal
            ? Orientation.Horizontal
            : Orientation.Vertical;
        _stackPanel.Spacing = 6 * scale;
        _stackPanel.Margin = new Thickness(6 * scale);

        _stackPanel.Children.Clear();

        foreach (var entry in GetOrderedEntries())
        {
            var iconBlock = new TextBlock
            {
                Text = ConvertIcon(entry.Icon),
                FontSize = 18 * scale,
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };

            var nameBlock = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(entry.Name) ? "触发" : entry.Name,
                FontSize = 10 * scale,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 100 * scale,
                Margin = new Thickness(0, 2 * scale, 0, 0)
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
                Foreground = Brushes.White,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };

            button.Click += (_, _) => entry.TriggerAction();
            _stackPanel.Children.Add(button);
        }
    }

    private List<FloatingWindowEntry> GetOrderedEntries()
    {
        var order = _configHandler.Data.FloatingWindowButtonOrder ?? new List<string>();
        var values = _entries.Values.ToList();
        return values.OrderBy(x =>
            {
                var index = order.IndexOf(x.ButtonId);
                return index < 0 ? int.MaxValue : index;
            })
            .ThenBy(x => x.ButtonId)
            .ToList();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_window == null || !e.GetCurrentPoint(_window).Properties.IsLeftButtonPressed)
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
        if (_window == null || !_pointerPressed || _dragInitiated)
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
        _pointerPressed = false;
        _dragInitiated = false;
        _lastPressedArgs = null;
        SavePosition();
    }

    private void SavePosition()
    {
        if (_window == null)
        {
            return;
        }

        _configHandler.Data.FloatingWindowPositionX = _window.Position.X;
        _configHandler.Data.FloatingWindowPositionY = _window.Position.Y;
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
}

public record FloatingWindowEntry(string ButtonId, string Icon, string Name, Action TriggerAction);
