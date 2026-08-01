using System;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace SystemTools.Views;

public partial class AiVoiceConversationOverlayWindow : Window
{
    private bool _allowClose;
    private readonly IDisposable _windowStateSubscription;

    public AiVoiceConversationOverlayWindow()
        : this(new PixelPoint(0, 0), 1, 1, isDark: false, opacity: 0.5)
    {
    }

    public AiVoiceConversationOverlayWindow(
        PixelPoint position,
        double width,
        double height,
        bool isDark,
        double opacity)
    {
        InitializeComponent();
        Position = position;
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Topmost = true;
        ApplyTheme(isDark, opacity);
        _windowStateSubscription = this.GetPropertyChangedObservable(WindowStateProperty).Subscribe(_ =>
        {
            if (WindowState == WindowState.Normal)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                WindowState = WindowState.Normal;
                Topmost = true;
                Activate();
            }, DispatcherPriority.MaxValue);
        });
    }

    public event EventHandler? EscapePressed;

    public void SetStatus(string status, string? detail = null)
    {
        StatusText.Text = status;
        DetailText.Text = detail ?? string.Empty;
        DetailText.IsVisible = !string.IsNullOrWhiteSpace(detail);
    }

    public void UpdateAppearance(bool isDark, double opacity) =>
        ApplyTheme(isDark, opacity);

    public void CloseFromOwner()
    {
        if (_allowClose)
        {
            return;
        }

        _allowClose = true;
        _windowStateSubscription.Dispose();
        Close();
    }

    private void ApplyTheme(bool isDark, double opacity)
    {
        RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
        if (!double.IsFinite(opacity))
        {
            opacity = 0.5;
        }

        var alpha = (byte)Math.Clamp(Math.Round(opacity * 255), 0, 255);
        var color = isDark ? Colors.Black : Colors.White;
        var foreground = isDark ? Colors.White : Colors.Black;
        RootBorder.Background = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        StatusText.Foreground = new SolidColorBrush(foreground);
        DetailText.Foreground = new SolidColorBrush(foreground);
    }

    private void Window_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            EscapePressed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Window_OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
        }
    }
}
