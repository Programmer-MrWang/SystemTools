using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ClassIsland.Core.Controls;
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

        _stackPanel.Orientation = Orientation.Vertical;
        _stackPanel.Spacing = 6 * scale;
        _stackPanel.Margin = new Thickness(6 * scale);
        _stackPanel.HorizontalAlignment = HorizontalAlignment.Center;

        _stackPanel.Children.Clear();

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
                FontSize = 22 * scale,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var nameBlock = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(entry.Name) ? "触发" : entry.Name,
                FontSize = 12 * scale,
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

        _configHandler.Data.FloatingWindowButtonRows = rows
            .Select(r => r.Select(x => x.ButtonId).ToList())
            .ToList();
        _configHandler.Data.FloatingWindowButtonOrder = rows
            .SelectMany(r => r)
            .Select(x => x.ButtonId)
            .ToList();

        return rows;
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
