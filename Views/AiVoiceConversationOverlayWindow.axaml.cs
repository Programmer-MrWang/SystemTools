using System;
using System.Threading.Tasks;
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
    private bool _exitAnimationStarted;
    private readonly IDisposable _windowStateSubscription;
    private readonly double _cornerRadius;
    private readonly PixelPoint _initialPosition;
    private readonly double _initialWidth;
    private readonly double _initialHeight;

    public AiVoiceConversationOverlayWindow()
        : this(new PixelPoint(0, 0), 1, 1, isDark: false, opacity: 0.5, cornerRadius: 8.0)
    {
    }

    public AiVoiceConversationOverlayWindow(
        PixelPoint position,
        double width,
        double height,
        bool isDark,
        double opacity,
        double cornerRadius)
    {
        InitializeComponent();
        Position = position;
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        _initialPosition = Position;
        _initialWidth = Width;
        _initialHeight = Height;
        Topmost = true;
        _cornerRadius = Math.Max(0, cornerRadius);
        ApplyTheme(isDark, opacity);
        RootBorder.CornerRadius = new CornerRadius(_cornerRadius + 10);
        Waveform.SetDarkTheme(isDark);
        Waveform.SetListening(false);
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

    public void SetListening(bool isListening) => Waveform.SetListening(isListening);

    public void SetAudioLevel(double level) => Waveform.SetAudioLevel(level);

    /// <summary>
    /// Starts at the captured host size, then materializes the larger listening
    /// surface around its center so the overlay never jumps away from its source.
    /// </summary>
    public async Task PlayEntranceAsync()
    {
        var startWidth = Math.Max(1, Width);
        var startHeight = Math.Max(1, Height);
        var widthDelta = Math.Clamp(startWidth * 0.055, 56, 96);
        var heightDelta = Math.Clamp(startHeight * 0.17, 108, 184);
        var targetWidth = startWidth + widthDelta;
        var targetHeight = startHeight + heightDelta;
        var startPosition = Position;
        var targetPosition = new PixelPoint(
            startPosition.X - (int)Math.Round(widthDelta / 2),
            startPosition.Y);

        try
        {
            for (var frame = 1; frame <= 22; frame++)
            {
                if (_allowClose || !IsVisible)
                {
                    return;
                }

                var progress = frame / 22d;
                var eased = 1 - Math.Pow(1 - progress, 3);
                Width = startWidth + (targetWidth - startWidth) * eased;
                Height = startHeight + (targetHeight - startHeight) * eased;
                Position = new PixelPoint(
                    startPosition.X + (int)Math.Round((targetPosition.X - startPosition.X) * eased),
                    startPosition.Y + (int)Math.Round((targetPosition.Y - startPosition.Y) * eased));
                await Task.Delay(16);
            }

            if (_allowClose || !IsVisible)
            {
                return;
            }

            Width = targetWidth;
            Height = targetHeight;
            Position = targetPosition;
        }
        catch (InvalidOperationException)
        {
            // A cancellation can close the owner while the entrance is settling.
        }
    }

    public void UpdateAppearance(bool isDark, double opacity) => ApplyTheme(isDark, opacity);

    public void CloseFromOwner()
    {
        if (_allowClose || _exitAnimationStarted)
        {
            return;
        }

        _exitAnimationStarted = true;
        _ = PlayExitAsync();
    }

    private async Task PlayExitAsync()
    {
        try
        {
            if (!IsVisible)
            {
                FinalizeClose();
                return;
            }

            var startWidth = Width;
            var startHeight = Height;
            var startPosition = Position;
            for (var frame = 1; frame <= 18; frame++)
            {
                var progress = frame / 18d;
                var eased = 1 - Math.Pow(1 - progress, 3);
                Width = startWidth + (_initialWidth - startWidth) * eased;
                Height = startHeight + (_initialHeight - startHeight) * eased;
                Position = new PixelPoint(
                    startPosition.X + (int)Math.Round((_initialPosition.X - startPosition.X) * eased),
                    _initialPosition.Y);
                await Task.Delay(16);
            }
        }
        catch (InvalidOperationException)
        {
            // The host can close the overlay while the exit animation is settling.
        }
        finally
        {
            FinalizeClose();
        }
    }

    private void FinalizeClose()
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

        var alpha = (byte)Math.Clamp(Math.Round(Math.Max(0.58, opacity) * 255), 0, 245);
        var background = isDark
            ? new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new GradientStop(Color.FromArgb(alpha, 11, 19, 32), 0),
                    new GradientStop(Color.FromArgb((byte)Math.Max(110, alpha - 20), 20, 28, 47), 1)
                }
            }
            : new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = new GradientStops
                {
                    new GradientStop(Color.FromArgb(alpha, 246, 250, 255), 0),
                    new GradientStop(Color.FromArgb((byte)Math.Max(110, alpha - 20), 227, 238, 250), 1)
                }
            };

        var foreground = isDark ? Colors.White : Color.FromRgb(20, 27, 38);
        RootBorder.Background = background;
        RootBorder.BorderBrush = new SolidColorBrush(Color.FromArgb((byte)Math.Min(210, alpha + 20), foreground.R, foreground.G, foreground.B));
        RootBorder.BorderThickness = new Thickness(1);
        StatusText.Foreground = new SolidColorBrush(foreground);
        DetailText.Foreground = new SolidColorBrush(foreground);
        Waveform.SetDarkTheme(isDark);
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
