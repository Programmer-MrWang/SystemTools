using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SystemTools.ConfigHandlers;

namespace SystemTools.Controls;

/// <summary>
/// A non-interactive liquid-glass surface that follows the AI conversation's
/// shared material settings. Keeping the copy in one control also makes the
/// repeated message templates cheap to configure.
/// </summary>
public sealed class AiChatGlassSurface : LiquidGlassAvaloniaUI.LiquidGlassSurface
{
    public static readonly StyledProperty<LiquidGlassSettings?> SettingsProperty =
        AvaloniaProperty.Register<AiChatGlassSurface, LiquidGlassSettings?>(nameof(Settings));

    private LiquidGlassSettings? _observedSettings;

    public LiquidGlassSettings? Settings
    {
        get => GetValue(SettingsProperty);
        set => SetValue(SettingsProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeToSettings();
        ApplySettings();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UnsubscribeFromSettings();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SettingsProperty)
        {
            SubscribeToSettings();
            ApplySettings();
        }
    }

    private void SubscribeToSettings()
    {
        if (ReferenceEquals(_observedSettings, Settings))
        {
            return;
        }

        UnsubscribeFromSettings();
        _observedSettings = Settings;
        if (_observedSettings is not null)
        {
            _observedSettings.PropertyChanged += OnSettingsPropertyChanged;
        }
    }

    private void UnsubscribeFromSettings()
    {
        if (_observedSettings is not null)
        {
            _observedSettings.PropertyChanged -= OnSettingsPropertyChanged;
            _observedSettings = null;
        }
    }

    private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplySettings();
            return;
        }

        Dispatcher.UIThread.Post(ApplySettings);
    }

    private void ApplySettings()
    {
        if (Settings is not { } settings)
        {
            return;
        }

        BackdropZoom = settings.BackdropZoom;
        BackdropOffset = new Vector(settings.BackdropOffsetX, settings.BackdropOffsetY);
        RefractionHeight = settings.RefractionHeight;
        RefractionAmount = settings.RefractionAmount;
        DepthEffect = settings.DepthEffect;
        ChromaticAberration = settings.ChromaticAberration;
        BlurRadius = settings.BlurRadius;
        Vibrancy = settings.Vibrancy;
        Brightness = settings.Brightness;
        Contrast = settings.Contrast;
        ExposureEv = settings.ExposureEv;
        GammaPower = settings.GammaPower;
        BackdropOpacity = settings.BackdropOpacity;
        TintColor = ParseColor(settings.TintColor, Colors.Transparent);
        // SurfaceColor is intentionally owned by the theme resource in XAML.
        // It stays faintly tinted in both modes while the backdrop remains visible.
        ProgressiveBlurEnabled = settings.ProgressiveBlurEnabled;
        ProgressiveBlurStart = settings.ProgressiveBlurStart;
        ProgressiveBlurEnd = settings.ProgressiveBlurEnd;
        ProgressiveTintColor = ParseColor(settings.ProgressiveTintColor, Colors.Transparent);
        ProgressiveTintIntensity = settings.ProgressiveTintIntensity;
        AdaptiveLuminanceEnabled = settings.AdaptiveLuminanceEnabled;
        AdaptiveLuminanceUpdateIntervalMs = settings.AdaptiveLuminanceUpdateIntervalMs;
        AdaptiveLuminanceSmoothing = settings.AdaptiveLuminanceSmoothing;
        HighlightEnabled = settings.HighlightEnabled;
        HighlightWidth = settings.HighlightWidth;
        HighlightBlurRadius = settings.HighlightBlurRadius;
        HighlightOpacity = settings.HighlightOpacity;
        HighlightAngle = settings.HighlightAngle;
        HighlightFalloff = settings.HighlightFalloff;
        ShadowEnabled = settings.ShadowEnabled;
        ShadowRadius = Math.Min(settings.ShadowRadius, 14);
        ShadowOffset = new Vector(0, 2);
        ShadowColor = ParseColor(settings.ShadowColor, Color.FromArgb(26, 0, 0, 0));
        ShadowOpacity = Math.Min(settings.ShadowOpacity, 0.55);
        InnerShadowEnabled = settings.InnerShadowEnabled;
        InnerShadowRadius = Math.Min(settings.InnerShadowRadius, 24);
        InnerShadowOffset = new Vector(settings.InnerShadowOffsetX, settings.InnerShadowOffsetY);
        InnerShadowColor = ParseColor(settings.InnerShadowColor, Color.FromArgb(38, 0, 0, 0));
        InnerShadowOpacity = settings.InnerShadowOpacity;
    }

    private static Color ParseColor(string? value, Color fallback) =>
        Color.TryParse(value, out var color) ? color : fallback;
}
