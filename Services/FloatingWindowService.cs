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
        _entries[trigger] = new FloatingWindowEntry(trigger.GetButtonId(), trigger.GetIcon(),
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

        _stackPanel.Orientation = _configHandler.Data.FloatingWindowHorizontal
            ? Orientation.Horizontal
            : Orientation.Vertical;

        _stackPanel.Children.Clear();

        foreach (var entry in GetOrderedEntries())
        {
            var button = new Button
            {
                Content = new TextBlock
                {
                    Text = ConvertIcon(entry.Icon),
                    FontSize = 18,
                    FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                Width = 36,
                Height = 36,
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                Tag = entry
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

        if (e.Source is not Button)
        {
            _window.BeginMoveDrag(e);
        }
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

public record FloatingWindowEntry(string ButtonId, string Icon, Action TriggerAction);
