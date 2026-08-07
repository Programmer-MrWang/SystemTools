using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace SystemTools.ConfigHandlers;

public sealed class LiquidGlassSettings : INotifyPropertyChanged
{
    private bool _suppressNotifications;
    private double _cornerRadius = 18;
    private double _backdropRefreshIntervalMs = 50;
    private double _backdropZoom = 1;
    private double _backdropOffsetX;
    private double _backdropOffsetY;
    private double _refractionHeight = 12;
    private double _refractionAmount = 24;
    private bool _depthEffect;
    private bool _chromaticAberration;
    private double _blurRadius = 2;
    private double _vibrancy = 1.5;
    private double _brightness;
    private double _contrast = 1;
    private double _exposureEv;
    private double _gammaPower = 1;
    private double _backdropOpacity = 1;
    private string _tintColor = "#00000000";
    private string _surfaceColor = "#00000000";
    private bool _progressiveBlurEnabled;
    private double _progressiveBlurStart = 0.5;
    private double _progressiveBlurEnd = 1;
    private string _progressiveTintColor = "#00000000";
    private double _progressiveTintIntensity = 0.8;
    private bool _adaptiveLuminanceEnabled;
    private double _adaptiveLuminanceUpdateIntervalMs = 250;
    private double _adaptiveLuminanceSmoothing = 0.2;
    private bool _highlightEnabled = true;
    private double _highlightWidth = 0.5;
    private double _highlightBlurRadius = 0.25;
    private double _highlightOpacity = 0.5;
    private double _highlightAngle = 45;
    private double _highlightFalloff = 1;
    private bool _shadowEnabled = true;
    private double _shadowRadius = 24;
    private double _shadowOffsetX;
    private double _shadowOffsetY = 4;
    private string _shadowColor = "#1A000000";
    private double _shadowOpacity = 1;
    private bool _innerShadowEnabled;
    private double _innerShadowRadius = 24;
    private double _innerShadowOffsetX;
    private double _innerShadowOffsetY = 24;
    private string _innerShadowColor = "#26000000";
    private double _innerShadowOpacity = 1;

    public event PropertyChangedEventHandler? PropertyChanged;

    [JsonPropertyName("cornerRadius")]
    public double CornerRadius { get => _cornerRadius; set => Set(ref _cornerRadius, Clamp(value, 0, 96)); }

    [JsonPropertyName("backdropRefreshIntervalMs")]
    public double BackdropRefreshIntervalMs { get => _backdropRefreshIntervalMs; set => Set(ref _backdropRefreshIntervalMs, Clamp(value, 5, 200)); }

    [JsonPropertyName("backdropZoom")]
    public double BackdropZoom { get => _backdropZoom; set => Set(ref _backdropZoom, Clamp(value, 0.1, 10)); }

    [JsonPropertyName("backdropOffsetX")]
    public double BackdropOffsetX { get => _backdropOffsetX; set => Set(ref _backdropOffsetX, Clamp(value, -500, 500)); }

    [JsonPropertyName("backdropOffsetY")]
    public double BackdropOffsetY { get => _backdropOffsetY; set => Set(ref _backdropOffsetY, Clamp(value, -500, 500)); }

    [JsonPropertyName("refractionHeight")]
    public double RefractionHeight { get => _refractionHeight; set => Set(ref _refractionHeight, Clamp(value, 0, 100)); }

    [JsonPropertyName("refractionAmount")]
    public double RefractionAmount { get => _refractionAmount; set => Set(ref _refractionAmount, Clamp(value, 0, 200)); }

    [JsonPropertyName("depthEffect")]
    public bool DepthEffect { get => _depthEffect; set => Set(ref _depthEffect, value); }

    [JsonPropertyName("chromaticAberration")]
    public bool ChromaticAberration { get => _chromaticAberration; set => Set(ref _chromaticAberration, value); }

    [JsonPropertyName("blurRadius")]
    public double BlurRadius { get => _blurRadius; set => Set(ref _blurRadius, Clamp(value, 0, 64)); }

    [JsonPropertyName("vibrancy")]
    public double Vibrancy { get => _vibrancy; set => Set(ref _vibrancy, Clamp(value, 0, 4)); }

    [JsonPropertyName("brightness")]
    public double Brightness { get => _brightness; set => Set(ref _brightness, Clamp(value, -1, 1)); }

    [JsonPropertyName("contrast")]
    public double Contrast { get => _contrast; set => Set(ref _contrast, Clamp(value, 0, 4)); }

    [JsonPropertyName("exposureEv")]
    public double ExposureEv { get => _exposureEv; set => Set(ref _exposureEv, Clamp(value, -4, 4)); }

    [JsonPropertyName("gammaPower")]
    public double GammaPower { get => _gammaPower; set => Set(ref _gammaPower, Clamp(value, 0.1, 4)); }

    [JsonPropertyName("backdropOpacity")]
    public double BackdropOpacity { get => _backdropOpacity; set => Set(ref _backdropOpacity, Clamp(value, 0, 1)); }

    [JsonPropertyName("tintColor")]
    public string TintColor { get => _tintColor; set => Set(ref _tintColor, NormalizeColor(value)); }

    [JsonPropertyName("surfaceColor")]
    public string SurfaceColor { get => _surfaceColor; set => Set(ref _surfaceColor, NormalizeColor(value)); }

    [JsonPropertyName("progressiveBlurEnabled")]
    public bool ProgressiveBlurEnabled { get => _progressiveBlurEnabled; set => Set(ref _progressiveBlurEnabled, value); }

    [JsonPropertyName("progressiveBlurStart")]
    public double ProgressiveBlurStart { get => _progressiveBlurStart; set => Set(ref _progressiveBlurStart, Clamp(value, 0, 1)); }

    [JsonPropertyName("progressiveBlurEnd")]
    public double ProgressiveBlurEnd { get => _progressiveBlurEnd; set => Set(ref _progressiveBlurEnd, Clamp(value, 0, 1)); }

    [JsonPropertyName("progressiveTintColor")]
    public string ProgressiveTintColor { get => _progressiveTintColor; set => Set(ref _progressiveTintColor, NormalizeColor(value)); }

    [JsonPropertyName("progressiveTintIntensity")]
    public double ProgressiveTintIntensity { get => _progressiveTintIntensity; set => Set(ref _progressiveTintIntensity, Clamp(value, 0, 1)); }

    [JsonPropertyName("adaptiveLuminanceEnabled")]
    public bool AdaptiveLuminanceEnabled { get => _adaptiveLuminanceEnabled; set => Set(ref _adaptiveLuminanceEnabled, value); }

    [JsonPropertyName("adaptiveLuminanceUpdateIntervalMs")]
    public double AdaptiveLuminanceUpdateIntervalMs { get => _adaptiveLuminanceUpdateIntervalMs; set => Set(ref _adaptiveLuminanceUpdateIntervalMs, Clamp(value, 16, 5000)); }

    [JsonPropertyName("adaptiveLuminanceSmoothing")]
    public double AdaptiveLuminanceSmoothing { get => _adaptiveLuminanceSmoothing; set => Set(ref _adaptiveLuminanceSmoothing, Clamp(value, 0, 1)); }

    [JsonPropertyName("highlightEnabled")]
    public bool HighlightEnabled { get => _highlightEnabled; set => Set(ref _highlightEnabled, value); }

    [JsonPropertyName("highlightWidth")]
    public double HighlightWidth { get => _highlightWidth; set => Set(ref _highlightWidth, Clamp(value, 0, 12)); }

    [JsonPropertyName("highlightBlurRadius")]
    public double HighlightBlurRadius { get => _highlightBlurRadius; set => Set(ref _highlightBlurRadius, Clamp(value, 0, 12)); }

    [JsonPropertyName("highlightOpacity")]
    public double HighlightOpacity { get => _highlightOpacity; set => Set(ref _highlightOpacity, Clamp(value, 0, 1)); }

    [JsonPropertyName("highlightAngle")]
    public double HighlightAngle { get => _highlightAngle; set => Set(ref _highlightAngle, Clamp(value, 0, 360)); }

    [JsonPropertyName("highlightFalloff")]
    public double HighlightFalloff { get => _highlightFalloff; set => Set(ref _highlightFalloff, Clamp(value, 0, 8)); }

    [JsonPropertyName("shadowEnabled")]
    public bool ShadowEnabled { get => _shadowEnabled; set => Set(ref _shadowEnabled, value); }

    [JsonPropertyName("shadowRadius")]
    public double ShadowRadius { get => _shadowRadius; set => Set(ref _shadowRadius, Clamp(value, 0, 128)); }

    [JsonPropertyName("shadowOffsetX")]
    public double ShadowOffsetX { get => _shadowOffsetX; set => Set(ref _shadowOffsetX, Clamp(value, -200, 200)); }

    [JsonPropertyName("shadowOffsetY")]
    public double ShadowOffsetY { get => _shadowOffsetY; set => Set(ref _shadowOffsetY, Clamp(value, -200, 200)); }

    [JsonPropertyName("shadowColor")]
    public string ShadowColor { get => _shadowColor; set => Set(ref _shadowColor, NormalizeColor(value)); }

    [JsonPropertyName("shadowOpacity")]
    public double ShadowOpacity { get => _shadowOpacity; set => Set(ref _shadowOpacity, Clamp(value, 0, 1)); }

    [JsonPropertyName("innerShadowEnabled")]
    public bool InnerShadowEnabled { get => _innerShadowEnabled; set => Set(ref _innerShadowEnabled, value); }

    [JsonPropertyName("innerShadowRadius")]
    public double InnerShadowRadius { get => _innerShadowRadius; set => Set(ref _innerShadowRadius, Clamp(value, 0, 128)); }

    [JsonPropertyName("innerShadowOffsetX")]
    public double InnerShadowOffsetX { get => _innerShadowOffsetX; set => Set(ref _innerShadowOffsetX, Clamp(value, -200, 200)); }

    [JsonPropertyName("innerShadowOffsetY")]
    public double InnerShadowOffsetY { get => _innerShadowOffsetY; set => Set(ref _innerShadowOffsetY, Clamp(value, -200, 200)); }

    [JsonPropertyName("innerShadowColor")]
    public string InnerShadowColor { get => _innerShadowColor; set => Set(ref _innerShadowColor, NormalizeColor(value)); }

    [JsonPropertyName("innerShadowOpacity")]
    public double InnerShadowOpacity { get => _innerShadowOpacity; set => Set(ref _innerShadowOpacity, Clamp(value, 0, 1)); }

    public void Reset()
    {
        _suppressNotifications = true;
        try
        {
            CornerRadius = 18;
            BackdropRefreshIntervalMs = 50;
            BackdropZoom = 1;
            BackdropOffsetX = 0;
            BackdropOffsetY = 0;
            RefractionHeight = 12;
            RefractionAmount = 24;
            DepthEffect = false;
            ChromaticAberration = false;
            BlurRadius = 2;
            Vibrancy = 1.5;
            Brightness = 0;
            Contrast = 1;
            ExposureEv = 0;
            GammaPower = 1;
            BackdropOpacity = 1;
            TintColor = "#00000000";
            SurfaceColor = "#00000000";
            ProgressiveBlurEnabled = false;
            ProgressiveBlurStart = 0.5;
            ProgressiveBlurEnd = 1;
            ProgressiveTintColor = "#00000000";
            ProgressiveTintIntensity = 0.8;
            AdaptiveLuminanceEnabled = false;
            AdaptiveLuminanceUpdateIntervalMs = 250;
            AdaptiveLuminanceSmoothing = 0.2;
            HighlightEnabled = true;
            HighlightWidth = 0.5;
            HighlightBlurRadius = 0.25;
            HighlightOpacity = 0.5;
            HighlightAngle = 45;
            HighlightFalloff = 1;
            ShadowEnabled = true;
            ShadowRadius = 24;
            ShadowOffsetX = 0;
            ShadowOffsetY = 4;
            ShadowColor = "#1A000000";
            ShadowOpacity = 1;
            InnerShadowEnabled = false;
            InnerShadowRadius = 24;
            InnerShadowOffsetX = 0;
            InnerShadowOffsetY = 24;
            InnerShadowColor = "#26000000";
            InnerShadowOpacity = 1;
        }
        finally
        {
            _suppressNotifications = false;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        if (!_suppressNotifications)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        return true;
    }

    private static double Clamp(double value, double minimum, double maximum) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : minimum;

    private static string NormalizeColor(string? value) => value?.Trim() ?? string.Empty;
}
