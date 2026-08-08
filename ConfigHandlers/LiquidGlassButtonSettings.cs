using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace SystemTools.ConfigHandlers;

/// <summary>
/// Persisted interaction and shadow settings shared by the approval buttons.
/// </summary>
public sealed class LiquidGlassButtonSettings : INotifyPropertyChanged
{
    private double _scaleDip = 3.5;
    private bool _interactiveHighlightEnabled = true;
    private bool _shadowEnabled = true;
    private double _shadowRadius = 14;
    private double _shadowOffsetX;
    private double _shadowOffsetY = 2;
    private double _shadowOpacity = 0.55;

    public event PropertyChangedEventHandler? PropertyChanged;

    [JsonPropertyName("scaleDip")]
    public double ScaleDip
    {
        get => _scaleDip;
        set => Set(ref _scaleDip, Clamp(value, 0, 12));
    }

    [JsonPropertyName("interactiveHighlightEnabled")]
    public bool InteractiveHighlightEnabled
    {
        get => _interactiveHighlightEnabled;
        set => Set(ref _interactiveHighlightEnabled, value);
    }

    [JsonPropertyName("shadowEnabled")]
    public bool ShadowEnabled
    {
        get => _shadowEnabled;
        set => Set(ref _shadowEnabled, value);
    }

    [JsonPropertyName("shadowRadius")]
    public double ShadowRadius
    {
        get => _shadowRadius;
        set => Set(ref _shadowRadius, Clamp(value, 0, 64));
    }

    [JsonPropertyName("shadowOffsetX")]
    public double ShadowOffsetX
    {
        get => _shadowOffsetX;
        set => Set(ref _shadowOffsetX, Clamp(value, -32, 32));
    }

    [JsonPropertyName("shadowOffsetY")]
    public double ShadowOffsetY
    {
        get => _shadowOffsetY;
        set => Set(ref _shadowOffsetY, Clamp(value, -32, 32));
    }

    [JsonPropertyName("shadowOpacity")]
    public double ShadowOpacity
    {
        get => _shadowOpacity;
        set => Set(ref _shadowOpacity, Clamp(value, 0, 1));
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static double Clamp(double value, double minimum, double maximum) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : minimum;
}
