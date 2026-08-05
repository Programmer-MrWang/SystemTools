using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace SystemTools.Views;

/// <summary>
/// Display-synchronised voice surface. Listening uses a full-width waveform;
/// a user pause contracts the same ribbons into a continuously moving orb.
/// </summary>
public sealed class VoiceWaveformControl : Control
{
    private const double EnvelopePower = 2.4;
    private const double LevelResponsePower = 0.68;
    private const double AttackSmoothing = 0.34;
    private const double ReleaseSmoothing = 0.13;
    private const double MorphSpringAngularFrequency = 11.5;
    private const double OrbDiameter = 68;
    private const double PausedPhaseSpeed = 2.8;

    private static readonly Color[] RibbonColors =
    [
        Color.FromArgb(118, 255, 116, 151),
        Color.FromArgb(120, 240, 99, 177),
        Color.FromArgb(115, 79, 167, 255),
        Color.FromArgb(112, 218, 95, 255),
        Color.FromArgb(125, 91, 222, 255)
    ];

    private static readonly OrbLobeSpec[] OrbLobes =
    [
        new(-1.86, 0.75, 0.38, 0.27, -0.08, 0.3, 0.7),
        new(2.5, 0.72, 0.4, 0.22, 0.09, 1.45, 0.62),
        new(0.82, 0.82, 0.34, 0.24, -0.1, 2.65, 0.76),
        new(-0.32, 0.78, 0.42, 0.2, 0.1, 3.85, 0.62),
        new(-2.98, 0.82, 0.48, 0.16, 0.07, 5.0, 0.78)
    ];

    private readonly DispatcherTimer _timer;
    private VisualState _state;
    private double _audioLevel;
    private double _smoothedLevel;
    private double _phase;
    private double _morphProgress;
    private double _morphVelocity;
    private bool _isDark = true;
    private bool _reduceMotion;
    private long _lastTickTimestamp;
    private double _motionPreferenceRefreshElapsed;

    public VoiceWaveformControl()
    {
        ClipToBounds = true;
        _reduceMotion = SystemMotionPreferences.ShouldReduceMotion();
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, OnTick);
        AttachedToVisualTree += (_, _) =>
        {
            _reduceMotion = SystemMotionPreferences.ShouldReduceMotion();
            SnapMotionIfNeeded();
            _lastTickTimestamp = Stopwatch.GetTimestamp();
            _timer.Start();
        };
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    public void SetListening(bool isListening) =>
        SetState(isListening ? VisualState.Listening : VisualState.Idle);

    public void SetUserPaused() => SetState(VisualState.UserPaused);

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

        var morph = Math.Clamp(_morphProgress, 0, 1);
        var inset = 4d;
        var fullWidth = Math.Max(2, width - inset * 2);
        var fullHeight = Math.Max(2, height - inset * 2);
        var orbDiameter = Math.Max(8, Math.Min(OrbDiameter, fullHeight));
        var shapeWidth = Lerp(fullWidth, orbDiameter, morph);
        var shapeHeight = Lerp(fullHeight, orbDiameter, morph);
        var shapeRect = new Rect(
            (width - shapeWidth) / 2,
            (height - shapeHeight) / 2,
            shapeWidth,
            shapeHeight);
        var cornerRadius = Math.Min(shapeWidth, shapeHeight) * 0.5 * morph;
        var orbRect = new Rect(
            (width - orbDiameter) / 2,
            (height - orbDiameter) / 2,
            orbDiameter,
            orbDiameter);
        var orbOpacity = SmoothStep(0.08, 0.78, morph);

        using (context.PushClip(new RoundedRect(shapeRect, cornerRadius)))
        {
            if (orbOpacity > 0.001)
            {
                DrawOrbShell(context, orbRect, orbOpacity);
            }

            var speakingEnergy = Math.Pow(Math.Clamp(_smoothedLevel, 0, 1), LevelResponsePower);
            var idleEnergy = 1.4 + (Math.Sin(_phase * 0.68) + 1) * 0.65;
            var listeningEnergy = 3.2 + speakingEnergy * (height * 0.37);
            var flatEnergy = _state == VisualState.Idle ? idleEnergy : listeningEnergy;
            var transitionEnergy = Lerp(flatEnergy, orbDiameter * 0.18, morph);
            DrawMorphingRibbons(
                context,
                shapeRect,
                orbRect,
                transitionEnergy,
                morph);
        }

        if (orbOpacity > 0.001)
        {
            DrawOrbEdge(context, orbRect, orbOpacity);
        }
    }

    private void DrawMorphingRibbons(
        DrawingContext context,
        Rect waveRect,
        Rect orbRect,
        double energy,
        double morph)
    {
        const int sampleCount = 96;
        var shapeProgress = SmoothStep(0, 1, morph);
        var radius = orbRect.Width / 2;
        var livingCenter = Add(
            orbRect.Center,
            new Point(
                Math.Sin(_phase * 0.22) * radius * 0.055,
                Math.Cos(_phase * 0.18) * radius * 0.05));

        for (var ribbon = 0; ribbon < RibbonColors.Length; ribbon++)
        {
            var lobe = OrbLobes[ribbon];
            var angle = lobe.Angle +
                        Math.Sin(_phase * 0.34 + lobe.PhaseOffset) * 0.14 +
                        Math.Sin(_phase * 0.16 - lobe.PhaseOffset) * 0.045;
            var reach = radius *
                        (lobe.Reach + Math.Sin(_phase * 0.29 + lobe.PhaseOffset) * 0.055);
            var tail = radius *
                       (lobe.Tail + Math.Cos(_phase * 0.23 + lobe.PhaseOffset) * 0.035);
            var halfWidth = radius *
                            (lobe.Width + Math.Cos(_phase * 0.31 + lobe.PhaseOffset) * 0.025);
            var bend = radius *
                       (lobe.Bend + Math.Sin(_phase * 0.26 + lobe.PhaseOffset) * 0.03);
            var geometry = new StreamGeometry();
            using (var builder = geometry.Open())
            {
                for (var sample = 0; sample <= sampleCount; sample++)
                {
                    var perimeter = sample / (double)sampleCount;
                    var wavePoint = GetWaveBoundaryPoint(
                        waveRect,
                        energy,
                        morph,
                        ribbon,
                        perimeter);
                    var lobePoint = GetRoundedLobeBoundaryPoint(
                        livingCenter,
                        angle,
                        reach,
                        tail,
                        halfWidth,
                        bend,
                        perimeter,
                        lobe.PhaseOffset);
                    var point = Lerp(wavePoint, lobePoint, shapeProgress);
                    if (sample == 0)
                    {
                        builder.BeginFigure(point, true);
                    }
                    else
                    {
                        builder.LineTo(point);
                    }
                }

                builder.EndFigure(true);
            }

            using (context.PushOpacity(Lerp(1, lobe.Opacity, shapeProgress)))
            {
                context.DrawGeometry(new SolidColorBrush(RibbonColors[ribbon]), null, geometry);
            }
        }

        var centerLineOpacity = 1 - SmoothStep(0.06, 0.5, morph);
        if (centerLineOpacity <= 0.001)
        {
            return;
        }

        var centerLine = new StreamGeometry();
        using (var builder = centerLine.Open())
        {
            builder.BeginFigure(new Point(waveRect.Left, waveRect.Center.Y), false);
            for (var i = 0; i <= 64; i++)
            {
                var position = i / 64d;
                var x = waveRect.Left + waveRect.Width * position;
                var envelope = GetEnvelope(position);
                var harmonic = Math.Sin(position * Math.PI * 3.8 + _phase * 0.92) *
                               energy * envelope * 0.18;
                builder.LineTo(new Point(x, waveRect.Center.Y - harmonic));
            }
        }

        var centerColor = _isDark
            ? Color.FromArgb(220, 244, 252, 255)
            : Color.FromArgb(205, 27, 43, 61);
        using (context.PushOpacity(centerLineOpacity))
        {
            context.DrawGeometry(null, new Pen(new SolidColorBrush(centerColor), 1.2), centerLine);
        }
    }

    private Point GetWaveBoundaryPoint(
        Rect rect,
        double energy,
        double morph,
        int ribbon,
        double perimeter)
    {
        var isUpperEdge = perimeter <= 0.5;
        var position = isUpperEdge ? perimeter * 2 : (1 - perimeter) * 2;
        var direction = isUpperEdge ? -1 : 1;
        var tint = ribbon * 0.83;
        var envelope = GetEnvelope(position);
        var harmonic = Math.Sin(
            position * Math.PI * (2.1 + ribbon * 0.14) +
            _phase * (0.66 + ribbon * 0.045) + tint);
        var detail = Math.Sin(
            position * Math.PI * (5.2 + ribbon * 0.25) -
            _phase * 0.86 + tint * 0.5) * (0.18 + morph * 0.08);
        var breathing = 1 + morph * Math.Sin(_phase * 0.31 + tint) * 0.08;
        var amplitude = energy * envelope * breathing * (0.72 + ribbon * 0.045);
        return new Point(
            rect.Left + rect.Width * position,
            rect.Center.Y + direction * amplitude * (harmonic + detail));
    }

    private static Point GetRoundedLobeBoundaryPoint(
        Point center,
        double angle,
        double reach,
        double tail,
        double halfWidth,
        double bend,
        double perimeter,
        double phaseOffset)
    {
        var direction = new Point(Math.Cos(angle), Math.Sin(angle));
        var normal = new Point(-direction.Y, direction.X);
        var theta = perimeter * Math.PI * 2;
        var longitudinalCenter = (reach - tail) * 0.5;
        var longitudinalRadius = (reach + tail) * 0.5;
        var along = longitudinalCenter - Math.Cos(theta) * longitudinalRadius;
        var widthVariation = 0.94 + Math.Cos(theta + phaseOffset) * 0.06;
        var across = Math.Sin(theta) * halfWidth * widthVariation;
        var travelProgress = Math.Clamp((along + tail) / (reach + tail), 0, 1);
        var curvedCenter = Math.Sin(travelProgress * Math.PI) * bend;
        return Add(
            Add(center, Scale(direction, along)),
            Scale(normal, across + curvedCenter));
    }

    private void DrawOrbShell(DrawingContext context, Rect orbRect, double opacity)
    {
        var driftX = 0.5 + Math.Sin(_phase * 0.09) * 0.055;
        var driftY = 0.5 + Math.Cos(_phase * 0.075) * 0.045;
        var shell = new RadialGradientBrush
        {
            Center = new RelativePoint(driftX, driftY, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(
                driftX - 0.08,
                driftY - 0.07,
                RelativeUnit.Relative),
            GradientStops = _isDark
                ? new GradientStops
                {
                    new(Color.FromArgb(54, 82, 207, 244), 0),
                    new(Color.FromArgb(46, 91, 59, 147), 0.5),
                    new(Color.FromArgb(32, 19, 33, 66), 0.8),
                    new(Color.FromArgb(10, 7, 17, 36), 1)
                }
                : new GradientStops
                {
                    new(Color.FromArgb(62, 197, 240, 250), 0),
                    new(Color.FromArgb(48, 135, 102, 201), 0.5),
                    new(Color.FromArgb(34, 65, 86, 137), 0.8),
                    new(Color.FromArgb(12, 30, 51, 86), 1)
                }
        };

        using (context.PushOpacity(opacity))
        {
            context.DrawEllipse(shell, null, orbRect);
        }
    }

    private void DrawOrbEdge(DrawingContext context, Rect orbRect, double opacity)
    {
        var center = orbRect.Center;
        var radius = orbRect.Width / 2;
        const int segmentCount = 24;
        for (var band = 0; band < 2; band++)
        {
            var bandRadius = radius - (band == 0 ? 0.7 : 2.5);
            var bandWidth = band == 0 ? 0.85 : 2.0;
            var minimumAlpha = band == 0 ? 1.5 : 0.5;
            var maximumAlpha = band == 0 ? 30 : 8;
            for (var segment = 0; segment < segmentCount; segment++)
            {
                var startAngle = segment * Math.PI * 2 / segmentCount - 0.018;
                var endAngle = (segment + 1) * Math.PI * 2 / segmentCount + 0.018;
                var middleAngle = (startAngle + endAngle) * 0.5;
                var edgeSignal = 0.5 +
                                 Math.Sin(middleAngle * 2.1 + _phase * 0.16) * 0.34 +
                                 Math.Sin(middleAngle * 4.7 - _phase * 0.11) * 0.16;
                var visibility = SmoothStep(0.16, 0.92, Math.Clamp(edgeSignal, 0, 1));
                var alpha = (byte)Math.Clamp(
                    Math.Round(opacity * Lerp(minimumAlpha, maximumAlpha, visibility)),
                    0,
                    255);
                if (alpha == 0)
                {
                    continue;
                }

                var hueMix = (Math.Sin(middleAngle + _phase * 0.07) + 1) * 0.5;
                var edgeColor = _isDark
                    ? Color.FromArgb(
                        alpha,
                        (byte)Math.Round(Lerp(91, 155, hueMix)),
                        (byte)Math.Round(Lerp(192, 129, hueMix)),
                        255)
                    : Color.FromArgb(
                        alpha,
                        (byte)Math.Round(Lerp(47, 115, hueMix)),
                        (byte)Math.Round(Lerp(128, 78, hueMix)),
                        (byte)Math.Round(Lerp(181, 203, hueMix)));
                var geometry = CreateArcSegmentGeometry(
                    center,
                    bandRadius,
                    startAngle,
                    endAngle);
                context.DrawGeometry(
                    null,
                    new Pen(new SolidColorBrush(edgeColor), bandWidth),
                    geometry);
            }
        }
    }

    private static StreamGeometry CreateArcSegmentGeometry(
        Point center,
        double radius,
        double startAngle,
        double endAngle)
    {
        const int subdivisionCount = 5;
        var geometry = new StreamGeometry();
        using var builder = geometry.Open();
        builder.BeginFigure(
            new Point(
                center.X + Math.Cos(startAngle) * radius,
                center.Y + Math.Sin(startAngle) * radius),
            false);
        for (var subdivision = 1; subdivision <= subdivisionCount; subdivision++)
        {
            var progress = subdivision / (double)subdivisionCount;
            var angle = Lerp(startAngle, endAngle, progress);
            builder.LineTo(new Point(
                center.X + Math.Cos(angle) * radius,
                center.Y + Math.Sin(angle) * radius));
        }

        return geometry;
    }

    private void SetState(VisualState state)
    {
        RefreshMotionPreference();
        _state = state;
        if (state != VisualState.Listening)
        {
            _audioLevel = 0;
        }

        if (_reduceMotion)
        {
            _morphProgress = state == VisualState.UserPaused ? 1 : 0;
            _morphVelocity = 0;
        }

        InvalidateVisual();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var timestamp = Stopwatch.GetTimestamp();
        var elapsed = _lastTickTimestamp == 0
            ? 1d / 60
            : Stopwatch.GetElapsedTime(_lastTickTimestamp, timestamp).TotalSeconds;
        _lastTickTimestamp = timestamp;
        var deltaTime = Math.Clamp(elapsed, 1d / 240, 0.05);
        _motionPreferenceRefreshElapsed += elapsed;
        if (_motionPreferenceRefreshElapsed >= 1)
        {
            _motionPreferenceRefreshElapsed = 0;
            RefreshMotionPreference();
        }

        var morphTarget = _state == VisualState.UserPaused ? 1d : 0d;

        if (_reduceMotion)
        {
            _morphProgress = morphTarget;
            _morphVelocity = 0;
        }
        else
        {
            _phase += deltaTime * Lerp(5.25, PausedPhaseSpeed, Math.Clamp(_morphProgress, 0, 1));
            UpdateCriticalSpring(
                ref _morphProgress,
                ref _morphVelocity,
                morphTarget,
                MorphSpringAngularFrequency,
                deltaTime);
        }

        var smoothing = _state != VisualState.Listening
            ? 0.075
            : _audioLevel > _smoothedLevel ? AttackSmoothing : ReleaseSmoothing;
        _smoothedLevel += (_audioLevel - _smoothedLevel) * smoothing;
        InvalidateVisual();
    }

    private void SnapMotionIfNeeded()
    {
        if (!_reduceMotion)
        {
            return;
        }

        _morphProgress = _state == VisualState.UserPaused ? 1 : 0;
        _morphVelocity = 0;
    }

    private void RefreshMotionPreference()
    {
        var reduceMotion = SystemMotionPreferences.ShouldReduceMotion();
        if (_reduceMotion == reduceMotion)
        {
            return;
        }

        _reduceMotion = reduceMotion;
        SnapMotionIfNeeded();
    }

    private static void UpdateCriticalSpring(
        ref double value,
        ref double velocity,
        double target,
        double angularFrequency,
        double deltaTime)
    {
        var displacement = value - target;
        var springTerm = velocity + angularFrequency * displacement;
        var decay = Math.Exp(-angularFrequency * deltaTime);
        var nextDisplacement = (displacement + springTerm * deltaTime) * decay;
        velocity = (velocity - angularFrequency * springTerm * deltaTime) * decay;
        value = target + nextDisplacement;

        if (Math.Abs(value - target) < 0.0005 && Math.Abs(velocity) < 0.005)
        {
            value = target;
            velocity = 0;
        }
    }

    private static double GetEnvelope(double position)
    {
        var taper = Math.Max(0, Math.Sin(Math.PI * position));
        return Math.Pow(taper, EnvelopePower);
    }

    private static double Lerp(double from, double to, double progress) =>
        from + (to - from) * progress;

    private static Point Lerp(Point from, Point to, double progress) =>
        new(
            Lerp(from.X, to.X, progress),
            Lerp(from.Y, to.Y, progress));

    private static double SmoothStep(double start, double end, double value)
    {
        var progress = Math.Clamp((value - start) / (end - start), 0, 1);
        return progress * progress * (3 - 2 * progress);
    }

    private static Point Add(Point left, Point right) =>
        new(left.X + right.X, left.Y + right.Y);

    private static Point Scale(Point point, double scale) =>
        new(point.X * scale, point.Y * scale);

    private readonly record struct OrbLobeSpec(
        double Angle,
        double Reach,
        double Tail,
        double Width,
        double Bend,
        double PhaseOffset,
        double Opacity);

    private enum VisualState
    {
        Idle,
        Listening,
        UserPaused
    }
}
