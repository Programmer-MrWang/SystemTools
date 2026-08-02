using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace SystemTools.Views;

/// <summary>
/// Lightweight display-synchronised voice waveform. The worker supplies a
/// normalized RMS level; a small idle drift keeps silence feeling alive.
/// </summary>
public sealed class VoiceWaveformControl : Control
{
    private static readonly Color[] RibbonColors =
    [
        Color.FromArgb(115, 91, 222, 255),
        Color.FromArgb(105, 79, 167, 255),
        Color.FromArgb(105, 218, 95, 255),
        Color.FromArgb(110, 240, 99, 177),
        Color.FromArgb(108, 255, 116, 151),
        Color.FromArgb(115, 255, 206, 107)
    ];

    private readonly DispatcherTimer _timer;
    private double _audioLevel;
    private double _smoothedLevel;
    private double _phase;
    private bool _isListening;
    private bool _isDark = true;

    public VoiceWaveformControl()
    {
        ClipToBounds = true;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, OnTick);
        AttachedToVisualTree += (_, _) => _timer.Start();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    public void SetListening(bool isListening)
    {
        _isListening = isListening;
        if (!isListening)
        {
            _audioLevel = 0;
        }

        InvalidateVisual();
    }

    public void SetAudioLevel(double level)
    {
        if (!double.IsFinite(level))
        {
            level = 0;
        }

        _audioLevel = Math.Clamp(level, 0, 1);
    }

    public void SetDarkTheme(bool isDark)
    {
        _isDark = isDark;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 2 || height <= 2)
        {
            return;
        }

        var center = height / 2;
        var speakingEnergy = Math.Clamp(_smoothedLevel, 0, 1);
        var idleEnergy = 1.4 + (Math.Sin(_phase * 0.68) + 1) * 0.65;
        var energy = _isListening ? 4 + speakingEnergy * (height * 0.42) : idleEnergy;

        for (var ribbon = 0; ribbon < RibbonColors.Length; ribbon++)
        {
            var geometry = new StreamGeometry();
            using (var builder = geometry.Open())
            {
                var tint = ribbon * 0.83;
                builder.BeginFigure(new Point(0, center), true);
                for (var i = 0; i <= 64; i++)
                {
                    var x = width * i / 64;
                    var position = i / 64d;
                    var envelope = Math.Pow(Math.Sin(Math.PI * position), 0.72);
                    var harmonic = Math.Sin(position * Math.PI * (2.1 + ribbon * 0.14) + _phase * (0.66 + ribbon * 0.045) + tint);
                    var detail = Math.Sin(position * Math.PI * (5.2 + ribbon * 0.25) - _phase * 0.86 + tint * 0.5) * 0.18;
                    var amplitude = energy * envelope * (0.72 + ribbon * 0.045);
                    builder.LineTo(new Point(x, center - amplitude * (harmonic + detail)));
                }

                for (var i = 64; i >= 0; i--)
                {
                    var x = width * i / 64;
                    var position = i / 64d;
                    var envelope = Math.Pow(Math.Sin(Math.PI * position), 0.72);
                    var harmonic = Math.Sin(position * Math.PI * (2.1 + ribbon * 0.14) + _phase * (0.66 + ribbon * 0.045) + tint);
                    var detail = Math.Sin(position * Math.PI * (5.2 + ribbon * 0.25) - _phase * 0.86 + tint * 0.5) * 0.18;
                    var amplitude = energy * envelope * (0.72 + ribbon * 0.045);
                    builder.LineTo(new Point(x, center + amplitude * (harmonic + detail)));
                }

                builder.EndFigure(true);
            }

            context.DrawGeometry(new SolidColorBrush(RibbonColors[ribbon]), null, geometry);
        }

        var centerLine = new StreamGeometry();
        using (var builder = centerLine.Open())
        {
            builder.BeginFigure(new Point(0, center), false);
            for (var i = 0; i <= 64; i++)
            {
                var x = width * i / 64;
                var position = i / 64d;
                var envelope = Math.Pow(Math.Sin(Math.PI * position), 0.72);
                var harmonic = Math.Sin(position * Math.PI * 3.8 + _phase * 0.92) * energy * envelope * 0.18;
                builder.LineTo(new Point(x, center - harmonic));
            }
        }

        var centerColor = _isDark
            ? Color.FromArgb(220, 244, 252, 255)
            : Color.FromArgb(205, 27, 43, 61);
        context.DrawGeometry(null, new Pen(new SolidColorBrush(centerColor), 1.2), centerLine);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _phase += 0.085;
        _smoothedLevel += (_audioLevel - _smoothedLevel) * (_isListening ? 0.22 : 0.075);
        InvalidateVisual();
    }
}
