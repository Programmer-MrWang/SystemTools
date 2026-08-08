using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using LiquidGlassAvaloniaUI;
using SystemTools.ConfigHandlers;
using SystemTools.Services;

namespace SystemTools.Views;

public partial class AiVoiceConversationOverlayWindow : Window
{
    private const double HeightSpringAngularFrequency = 18;

    private bool _allowClose;
    private bool _exitAnimationStarted;
    private bool _entranceAnimationRunning;
    private bool _heightAnimationRunning;
    private bool _transcriptMeasurePending;
    private bool _approvalMeasurePending;
    private bool _isListening;
    private bool _isUserPaused;
    private TaskCompletionSource<bool>? _approvalCompletion;
    private CancellationTokenRegistration _approvalCancellationRegistration;
    private readonly IDisposable _windowStateSubscription;
    private readonly DispatcherTimer _heightAnimationTimer;
    private readonly double _cornerRadius;
    private readonly PixelPoint _initialPosition;
    private readonly double _initialWidth;
    private readonly double _initialHeight;
    private WriteableBitmap? _liquidGlassBackdrop;
    private WriteableBitmap? _liquidGlassSpareBackdrop;
    private bool _isDark;
    private double _opacity;
    private int _appearanceStyle;
    private double _defaultExpandedHeight;
    private double _transcriptHeightDelta;
    private double _approvalHeightDelta;
    private double _targetHeight;
    private double _heightVelocity;
    private long _heightAnimationTimestamp;

    public AiVoiceConversationOverlayWindow()
        : this(
            new PixelPoint(0, 0),
            1,
            1,
            isDark: false,
            opacity: 0.5,
            cornerRadius: 8.0,
            appearanceStyle: 0,
            new LiquidGlassSettings(),
            new LiquidGlassButtonSettings(),
            liquidGlassBackdrop: null)
    {
    }

    public AiVoiceConversationOverlayWindow(
        PixelPoint position,
        double width,
        double height,
        bool isDark,
        double opacity,
        double cornerRadius,
        int appearanceStyle,
        LiquidGlassSettings liquidGlassSettings,
        LiquidGlassButtonSettings approvalButtonSettings,
        WriteableBitmap? liquidGlassBackdrop)
    {
        InitializeComponent();
        Position = position;
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        _initialPosition = Position;
        _initialWidth = Width;
        _initialHeight = Height;
        _defaultExpandedHeight = Height;
        _targetHeight = Height;
        Topmost = true;
        _cornerRadius = Math.Max(0, cornerRadius);
        _appearanceStyle = appearanceStyle == 1 ? 1 : 0;
        _liquidGlassBackdrop = liquidGlassBackdrop;
        LiquidGlassBackdropImage.Source = liquidGlassBackdrop;
        _heightAnimationTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(16),
            DispatcherPriority.Render,
            OnHeightAnimationTick);
        ApplyLiquidGlassSettings(liquidGlassSettings);
        ApplyApprovalButtonSettings(approvalButtonSettings);
        ApplyTheme(isDark, opacity);
        Waveform.SetDarkTheme(isDark);
        Waveform.SetListening(false);
        AutomationProperties.SetLiveSetting(StatusText, AutomationLiveSetting.Polite);
        ListeningToggleButton.Click += ListeningToggleButton_OnClick;
        UpdateListeningToggleButton();
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
    public event EventHandler<VoiceListeningToggleRequestedEventArgs>? ListeningToggleRequested;

    public void SetStatus(string status, string? detail = null)
    {
        StatusText.Text = status;
        StatusText.IsVisible = true;
        DetailText.Text = detail ?? string.Empty;
        DetailText.IsVisible = !string.IsNullOrWhiteSpace(detail);
        TranscriptText.Text = string.Empty;
        TranscriptText.IsVisible = false;
        SetTranscriptHeightDelta(0);
    }

    public void SetListening(bool isListening)
    {
        _isListening = isListening;
        _isUserPaused = false;
        Waveform.SetListening(isListening);
        UpdateListeningToggleButton();
    }

    public void ShowListening()
    {
        SetStatus("正在聆听……");
        _isListening = true;
        _isUserPaused = false;
        Waveform.SetListening(true);
        UpdateListeningToggleButton();
    }

    public void ShowStartingListening()
    {
        SetStatus("正在开启聆听……");
        _isListening = true;
        _isUserPaused = false;
        Waveform.SetListening(true);
        UpdateListeningToggleButton();
    }

    public void ShowUserPaused()
    {
        SetStatus("已停止聆听");
        _isListening = false;
        _isUserPaused = true;
        Waveform.SetUserPaused();
        UpdateListeningToggleButton();
    }

    public void SetRecognizedText(string text)
    {
        if (!_isListening || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        TranscriptText.Text = text.Trim();
        TranscriptText.IsVisible = true;
        StatusText.IsVisible = false;
        DetailText.IsVisible = false;
        QueueTranscriptHeightUpdate();
    }

    public void SetAudioLevel(double level) => Waveform.SetAudioLevel(level);

    private void ListeningToggleButton_OnClick(
        object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!ConversationContent.IsVisible || (!_isListening && !_isUserPaused))
        {
            UpdateListeningToggleButton();
            return;
        }

        var shouldListen = ListeningToggleButton.IsChecked == true;
        if (shouldListen)
        {
            ShowStartingListening();
        }
        else
        {
            ShowUserPaused();
        }

        ListeningToggleRequested?.Invoke(
            this,
            new VoiceListeningToggleRequestedEventArgs(shouldListen));
    }

    private void UpdateListeningToggleButton()
    {
        var isInteractive = _isListening || _isUserPaused;
        ListeningToggleButton.IsVisible = isInteractive;
        ListeningToggleButton.IsChecked = _isListening;
        var actionName = _isListening ? "停止聆听" : "恢复聆听";
        AutomationProperties.SetName(ListeningToggleButton, actionName);
    }

    public Task<bool> RequestToolApprovalAsync(
        string title,
        string summary,
        string details,
        string warning,
        CancellationToken cancellationToken)
    {
        if (_allowClose || _exitAnimationStarted || !IsVisible || cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        ResolveApproval(false, restoreConversation: false);
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _approvalCompletion = completion;
        ApprovalTitleText.Text = title;
        ApprovalSummaryText.Text = summary;
        ApprovalDetailsText.Text = details;
        ApprovalWarningText.Text = warning;
        SetListening(false);
        ConversationContent.IsVisible = false;
        ApprovalContent.IsVisible = true;
        QueueApprovalHeightUpdate();

        _approvalCancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                Dispatcher.UIThread.Post(() => ResolveApproval(completion, false));
            }
            catch
            {
                completion.TrySetResult(false);
            }
        });
        return completion.Task;
    }

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
        _defaultExpandedHeight = targetHeight;
        _targetHeight = targetHeight + CurrentContentHeightDelta;

        if (SystemMotionPreferences.ShouldReduceMotion())
        {
            Width = targetWidth;
            Height = _targetHeight;
            Position = targetPosition;
            return;
        }

        _entranceAnimationRunning = true;

        try
        {
            for (var frame = 1; frame <= 22; frame++)
            {
                if (_allowClose || _exitAnimationStarted || !IsVisible)
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
                await Task.Delay(8);
            }

            if (_allowClose || _exitAnimationStarted || !IsVisible)
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
        finally
        {
            _entranceAnimationRunning = false;
            if (!_allowClose && !_exitAnimationStarted && IsVisible)
            {
                if (TranscriptText.IsVisible)
                {
                    QueueTranscriptHeightUpdate();
                }

                AnimateHeightTo(_defaultExpandedHeight + CurrentContentHeightDelta);
            }
        }
    }

    public void UpdateAppearance(bool isDark, double opacity) => ApplyTheme(isDark, opacity);

    public void UpdateLiquidGlassAppearance(
        int appearanceStyle,
        LiquidGlassSettings settings,
        LiquidGlassButtonSettings approvalButtonSettings)
    {
        _appearanceStyle = appearanceStyle == 1 ? 1 : 0;
        if (_appearanceStyle != 1)
        {
            ReleaseLiquidGlassBackdrops();
        }

        ApplyLiquidGlassSettings(settings);
        ApplyApprovalButtonSettings(approvalButtonSettings);
        ApplyTheme(_isDark, _opacity);
    }

    public void UpdateLiquidGlassBackdrop(MainWindowBackgroundFrame frame)
    {
        var backdrop = LiquidGlassBackdropFactory.Update(frame, _liquidGlassSpareBackdrop);
        if (backdrop is null)
        {
            return;
        }

        var previous = _liquidGlassBackdrop;
        _liquidGlassBackdrop = backdrop;
        _liquidGlassSpareBackdrop = previous;
        LiquidGlassBackdropImage.Source = backdrop;
        ApplyTheme(_isDark, _opacity);
    }

    private void ReleaseLiquidGlassBackdrops()
    {
        LiquidGlassBackdropImage.Source = null;
        _liquidGlassBackdrop?.Dispose();
        _liquidGlassBackdrop = null;
        _liquidGlassSpareBackdrop?.Dispose();
        _liquidGlassSpareBackdrop = null;
    }

    public void CloseFromOwner()
    {
        if (_allowClose || _exitAnimationStarted)
        {
            return;
        }

        _exitAnimationStarted = true;
        ResolveApproval(false, restoreConversation: false);
        StopHeightAnimation();
        _ = PlayExitAsync();
    }

    private async Task PlayExitAsync()
    {
        if (SystemMotionPreferences.ShouldReduceMotion())
        {
            FinalizeClose();
            return;
        }

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
                await Task.Delay(8);
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
        StopHeightAnimation();
        _windowStateSubscription.Dispose();
        Close();
    }

    private void QueueTranscriptHeightUpdate()
    {
        if (_transcriptMeasurePending)
        {
            return;
        }

        _transcriptMeasurePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _transcriptMeasurePending = false;
            if (!TranscriptText.IsVisible)
            {
                return;
            }

            var layoutWidth = RootBorder.Bounds.Width;
            if (!double.IsFinite(layoutWidth) || layoutWidth <= 0)
            {
                layoutWidth = Width;
            }

            var contentWidth = Math.Max(1, layoutWidth - RootBorder.Padding.Left - RootBorder.Padding.Right);
            var availableWidth = double.IsFinite(TranscriptText.MaxWidth)
                ? Math.Min(TranscriptText.MaxWidth, contentWidth)
                : contentWidth;
            TranscriptText.Measure(new Size(availableWidth, double.PositiveInfinity));
            var singleLineHeight = double.IsFinite(TranscriptText.LineHeight) && TranscriptText.LineHeight > 0
                ? TranscriptText.LineHeight
                : TranscriptText.FontSize * 1.45;
            var requiredHeight = Math.Max(singleLineHeight, TranscriptText.DesiredSize.Height);
            SetTranscriptHeightDelta(Math.Ceiling(requiredHeight - singleLineHeight));
        }, DispatcherPriority.Loaded);
    }

    private void SetTranscriptHeightDelta(double heightDelta)
    {
        _transcriptHeightDelta = Math.Max(0, heightDelta);
        if (!_entranceAnimationRunning && !ApprovalContent.IsVisible)
        {
            AnimateHeightTo(_defaultExpandedHeight + _transcriptHeightDelta);
        }
    }

    private double CurrentContentHeightDelta =>
        ApprovalContent.IsVisible ? _approvalHeightDelta : _transcriptHeightDelta;

    private void QueueApprovalHeightUpdate()
    {
        if (_approvalMeasurePending)
        {
            return;
        }

        _approvalMeasurePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _approvalMeasurePending = false;
            if (!ApprovalContent.IsVisible)
            {
                return;
            }

            var layoutWidth = RootBorder.Bounds.Width;
            if (!double.IsFinite(layoutWidth) || layoutWidth <= 0)
            {
                layoutWidth = Width;
            }

            var contentWidth = Math.Max(1, layoutWidth - RootBorder.Padding.Left - RootBorder.Padding.Right);
            ApprovalContent.Measure(new Size(contentWidth, double.PositiveInfinity));
            var requiredHeight = ApprovalContent.DesiredSize.Height +
                                 RootBorder.Padding.Top + RootBorder.Padding.Bottom;
            _approvalHeightDelta = Math.Max(
                0,
                Math.Ceiling(requiredHeight - _defaultExpandedHeight));
            if (!_entranceAnimationRunning)
            {
                AnimateHeightTo(_defaultExpandedHeight + _approvalHeightDelta);
            }
        }, DispatcherPriority.Loaded);
    }

    private void ResolveApproval(bool approved, bool restoreConversation = true)
    {
        if (_approvalCompletion is not { } completion)
        {
            return;
        }

        ResolveApproval(completion, approved, restoreConversation);
    }

    private void ResolveApproval(
        TaskCompletionSource<bool> completion,
        bool approved,
        bool restoreConversation = true)
    {
        if (!ReferenceEquals(_approvalCompletion, completion))
        {
            return;
        }

        _approvalCompletion = null;
        _approvalCancellationRegistration.Dispose();
        _approvalCancellationRegistration = default;
        ApprovalContent.IsVisible = false;
        _approvalHeightDelta = 0;
        if (restoreConversation && !_allowClose && !_exitAnimationStarted)
        {
            ConversationContent.IsVisible = true;
            if (!_entranceAnimationRunning)
            {
                AnimateHeightTo(_defaultExpandedHeight + _transcriptHeightDelta);
            }
        }

        completion.TrySetResult(approved);
    }

    private void AnimateHeightTo(double targetHeight)
    {
        if (_allowClose || _exitAnimationStarted)
        {
            return;
        }

        _targetHeight = Math.Max(1, targetHeight);
        if (SystemMotionPreferences.ShouldReduceMotion())
        {
            Height = _targetHeight;
            StopHeightAnimation();
            return;
        }

        if (Math.Abs(Height - _targetHeight) < 0.25 && Math.Abs(_heightVelocity) < 1)
        {
            Height = _targetHeight;
            StopHeightAnimation();
            return;
        }

        if (_heightAnimationRunning)
        {
            return;
        }

        _heightAnimationRunning = true;
        _heightAnimationTimestamp = Stopwatch.GetTimestamp();
        _heightAnimationTimer.Start();
    }

    private void OnHeightAnimationTick(object? sender, EventArgs e)
    {
        if (_allowClose || _exitAnimationStarted || _entranceAnimationRunning)
        {
            StopHeightAnimation();
            return;
        }

        var timestamp = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(_heightAnimationTimestamp, timestamp).TotalSeconds;
        _heightAnimationTimestamp = timestamp;
        var deltaTime = Math.Clamp(elapsed, 1d / 240, 0.05);

        var displacement = Height - _targetHeight;
        var springTerm = _heightVelocity + HeightSpringAngularFrequency * displacement;
        var decay = Math.Exp(-HeightSpringAngularFrequency * deltaTime);
        var nextDisplacement = (displacement + springTerm * deltaTime) * decay;
        _heightVelocity = (_heightVelocity - HeightSpringAngularFrequency * springTerm * deltaTime) * decay;
        Height = Math.Max(1, _targetHeight + nextDisplacement);

        if (Math.Abs(Height - _targetHeight) < 0.25 && Math.Abs(_heightVelocity) < 1)
        {
            Height = _targetHeight;
            StopHeightAnimation();
        }
    }

    private void StopHeightAnimation()
    {
        _heightAnimationTimer.Stop();
        _heightAnimationRunning = false;
        _heightVelocity = 0;
    }

    private void ApplyTheme(bool isDark, double opacity)
    {
        _isDark = isDark;
        _opacity = opacity;
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
        var useLiquidGlass = _appearanceStyle == 1 && _liquidGlassBackdrop is not null;
        LiquidGlassBackdropClip.IsVisible = useLiquidGlass;
        LiquidGlassSurface.IsVisible = useLiquidGlass;
        RootBorder.CornerRadius = useLiquidGlass
            ? LiquidGlassSurface.CornerRadius
            : new CornerRadius(_cornerRadius + 10);
        RootBorder.Background = useLiquidGlass ? Brushes.Transparent : background;
        RootBorder.BorderBrush = useLiquidGlass
            ? Brushes.Transparent
            : new SolidColorBrush(Color.FromArgb(
                (byte)Math.Min(210, alpha + 20),
                foreground.R,
                foreground.G,
                foreground.B));
        RootBorder.BorderThickness = useLiquidGlass ? new Thickness(0) : new Thickness(1);
        StatusText.Foreground = new SolidColorBrush(foreground);
        DetailText.Foreground = new SolidColorBrush(foreground);
        TranscriptText.Foreground = new SolidColorBrush(foreground);
        ApprovalTitleText.Foreground = new SolidColorBrush(foreground);
        ApprovalSummaryText.Foreground = new SolidColorBrush(foreground);
        ApprovalDetailsText.Foreground = new SolidColorBrush(foreground);
        ApprovalWarningText.Foreground = new SolidColorBrush(foreground);
        DenyApprovalGlassButton.Foreground = new SolidColorBrush(foreground);
        ApproveApprovalGlassButton.Foreground = new SolidColorBrush(foreground);
        ClassicApprovalButtons.IsVisible = !useLiquidGlass;
        LiquidGlassApprovalButtons.IsVisible = useLiquidGlass;
        Waveform.SetDarkTheme(isDark);
    }

    private void ApplyLiquidGlassSettings(LiquidGlassSettings settings)
    {
        var cornerRadius = new CornerRadius(settings.CornerRadius);
        LiquidGlassBackdropClip.CornerRadius = cornerRadius;
        LiquidGlassSurface.CornerRadius = cornerRadius;
        LiquidGlassSurface.BackdropZoom = settings.BackdropZoom;
        LiquidGlassSurface.BackdropOffset = new Vector(settings.BackdropOffsetX, settings.BackdropOffsetY);
        LiquidGlassSurface.RefractionHeight = settings.RefractionHeight;
        LiquidGlassSurface.RefractionAmount = settings.RefractionAmount;
        LiquidGlassSurface.DepthEffect = settings.DepthEffect;
        LiquidGlassSurface.ChromaticAberration = settings.ChromaticAberration;
        LiquidGlassSurface.BlurRadius = settings.BlurRadius;
        LiquidGlassSurface.Vibrancy = settings.Vibrancy;
        LiquidGlassSurface.Brightness = settings.Brightness;
        LiquidGlassSurface.Contrast = settings.Contrast;
        LiquidGlassSurface.ExposureEv = settings.ExposureEv;
        LiquidGlassSurface.GammaPower = settings.GammaPower;
        LiquidGlassSurface.BackdropOpacity = settings.BackdropOpacity;
        LiquidGlassSurface.TintColor = ParseColor(settings.TintColor, Colors.Transparent);
        LiquidGlassSurface.SurfaceColor = ParseColor(settings.SurfaceColor, Colors.Transparent);
        LiquidGlassSurface.ProgressiveBlurEnabled = settings.ProgressiveBlurEnabled;
        LiquidGlassSurface.ProgressiveBlurStart = settings.ProgressiveBlurStart;
        LiquidGlassSurface.ProgressiveBlurEnd = settings.ProgressiveBlurEnd;
        LiquidGlassSurface.ProgressiveTintColor = ParseColor(settings.ProgressiveTintColor, Colors.Transparent);
        LiquidGlassSurface.ProgressiveTintIntensity = settings.ProgressiveTintIntensity;
        LiquidGlassSurface.AdaptiveLuminanceEnabled = settings.AdaptiveLuminanceEnabled;
        LiquidGlassSurface.AdaptiveLuminanceUpdateIntervalMs = settings.AdaptiveLuminanceUpdateIntervalMs;
        LiquidGlassSurface.AdaptiveLuminanceSmoothing = settings.AdaptiveLuminanceSmoothing;
        LiquidGlassSurface.HighlightEnabled = settings.HighlightEnabled;
        LiquidGlassSurface.HighlightWidth = settings.HighlightWidth;
        LiquidGlassSurface.HighlightBlurRadius = settings.HighlightBlurRadius;
        LiquidGlassSurface.HighlightOpacity = settings.HighlightOpacity;
        LiquidGlassSurface.HighlightAngle = settings.HighlightAngle;
        LiquidGlassSurface.HighlightFalloff = settings.HighlightFalloff;
        LiquidGlassSurface.ShadowEnabled = settings.ShadowEnabled;
        LiquidGlassSurface.ShadowRadius = settings.ShadowRadius;
        LiquidGlassSurface.ShadowOffset = new Vector(settings.ShadowOffsetX, settings.ShadowOffsetY);
        LiquidGlassSurface.ShadowColor = ParseColor(
            settings.ShadowColor,
            Color.FromArgb(26, 0, 0, 0));
        LiquidGlassSurface.ShadowOpacity = settings.ShadowOpacity;
        LiquidGlassSurface.InnerShadowEnabled = settings.InnerShadowEnabled;
        LiquidGlassSurface.InnerShadowRadius = settings.InnerShadowRadius;
        LiquidGlassSurface.InnerShadowOffset = new Vector(
            settings.InnerShadowOffsetX,
            settings.InnerShadowOffsetY);
        LiquidGlassSurface.InnerShadowColor = ParseColor(
            settings.InnerShadowColor,
            Color.FromArgb(38, 0, 0, 0));
        LiquidGlassSurface.InnerShadowOpacity = settings.InnerShadowOpacity;
    }

    private void ApplyApprovalButtonSettings(LiquidGlassButtonSettings settings)
    {
        ApplyApprovalButtonSettings(DenyApprovalButtonGlass, settings, "#00000000");
        ApplyApprovalButtonSettings(ApproveApprovalButtonGlass, settings, "#260078D4");
    }

    private void ApplyApprovalButtonSettings(
        LiquidGlassInteractiveSurface surface,
        LiquidGlassButtonSettings buttonSettings,
        string surfaceColor)
    {
        var windowSurface = this.LiquidGlassSurface;
        ApplyLiquidGlassSettingsToSurface(surface, windowSurface);
        surface.CornerRadius = new CornerRadius(999);
        surface.IsInteractive = true;
        surface.InteractiveHighlightEnabled = buttonSettings.InteractiveHighlightEnabled;
        surface.InteractiveMaxScaleDip = SystemMotionPreferences.ShouldReduceMotion()
            ? 0
            : buttonSettings.ScaleDip;
        surface.SurfaceColor = ParseColor(surfaceColor, Colors.Transparent);
        surface.ShadowEnabled = buttonSettings.ShadowEnabled;
        surface.ShadowRadius = buttonSettings.ShadowRadius;
        surface.ShadowOffset = new Vector(buttonSettings.ShadowOffsetX, buttonSettings.ShadowOffsetY);
        surface.ShadowOpacity = buttonSettings.ShadowOpacity;
    }

    private static void ApplyLiquidGlassSettingsToSurface(
        LiquidGlassSurface surface,
        LiquidGlassSurface source)
    {
        surface.BackdropZoom = source.BackdropZoom;
        surface.BackdropOffset = source.BackdropOffset;
        surface.RefractionHeight = source.RefractionHeight;
        surface.RefractionAmount = source.RefractionAmount;
        surface.DepthEffect = source.DepthEffect;
        surface.ChromaticAberration = source.ChromaticAberration;
        surface.BlurRadius = source.BlurRadius;
        surface.Vibrancy = source.Vibrancy;
        surface.Brightness = source.Brightness;
        surface.Contrast = source.Contrast;
        surface.ExposureEv = source.ExposureEv;
        surface.GammaPower = source.GammaPower;
        surface.BackdropOpacity = source.BackdropOpacity;
        surface.TintColor = source.TintColor;
        surface.ProgressiveBlurEnabled = source.ProgressiveBlurEnabled;
        surface.ProgressiveBlurStart = source.ProgressiveBlurStart;
        surface.ProgressiveBlurEnd = source.ProgressiveBlurEnd;
        surface.ProgressiveTintColor = source.ProgressiveTintColor;
        surface.ProgressiveTintIntensity = source.ProgressiveTintIntensity;
        surface.AdaptiveLuminanceEnabled = source.AdaptiveLuminanceEnabled;
        surface.AdaptiveLuminanceUpdateIntervalMs = source.AdaptiveLuminanceUpdateIntervalMs;
        surface.AdaptiveLuminanceSmoothing = source.AdaptiveLuminanceSmoothing;
        surface.HighlightEnabled = source.HighlightEnabled;
        surface.HighlightWidth = source.HighlightWidth;
        surface.HighlightBlurRadius = source.HighlightBlurRadius;
        surface.HighlightOpacity = source.HighlightOpacity;
        surface.HighlightAngle = source.HighlightAngle;
        surface.HighlightFalloff = source.HighlightFalloff;
        surface.ShadowColor = source.ShadowColor;
        surface.InnerShadowEnabled = source.InnerShadowEnabled;
        surface.InnerShadowRadius = source.InnerShadowRadius;
        surface.InnerShadowOffset = source.InnerShadowOffset;
        surface.InnerShadowColor = source.InnerShadowColor;
        surface.InnerShadowOpacity = source.InnerShadowOpacity;
    }

    private static Color ParseColor(string value, Color fallback) =>
        Color.TryParse(value, out var color) ? color : fallback;

    private void DenyApprovalButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => ResolveApproval(false);

    private void ApproveApprovalButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => ResolveApproval(true);

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
            return;
        }

        ReleaseLiquidGlassBackdrops();
    }
}

public sealed class VoiceListeningToggleRequestedEventArgs(bool shouldListen) : EventArgs
{
    public bool ShouldListen { get; } = shouldListen;
}
