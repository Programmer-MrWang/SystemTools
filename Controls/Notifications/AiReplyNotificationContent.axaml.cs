using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Animation.Easings;

namespace SystemTools.Controls.Notifications;

public partial class AiReplyNotificationContent : UserControl
{
    public const double ScrollSpeed = 160;

    private const double EstimatedViewportWidth = 720;
    private const double EstimatedCharacterWidth = 17;
    private double _lastAnimationDistance;
    private bool _isLoaded;

    public AiReplyNotificationContent() : this(string.Empty)
    {
    }

    public AiReplyNotificationContent(string text)
    {
        InitializeComponent();
        ReplyText.Text = text;
    }

    public static TimeSpan EstimateDisplayDuration(string text)
    {
        var estimatedDistance = EstimatedViewportWidth + Math.Max(1, text.Length) * EstimatedCharacterWidth;
        return TimeSpan.FromSeconds(estimatedDistance / ScrollSpeed);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        StartAnimation();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        _lastAnimationDistance = 0;
        ElementComposition.GetElementVisual(ReplyText)?.StopAnimation("Offset");
    }

    private void ReplyText_OnLayoutUpdated(object? sender, EventArgs e)
    {
        StartAnimation();
    }

    private void StartAnimation()
    {
        var viewportWidth = RootCanvas.Bounds.Width;
        var textWidth = ReplyText.Bounds.Width;
        var distance = viewportWidth + textWidth;
        if (!_isLoaded || viewportWidth <= 0 || textWidth <= 0 ||
            Math.Abs(distance - _lastAnimationDistance) < 0.5)
        {
            return;
        }

        var visual = ElementComposition.GetElementVisual(ReplyText);
        if (visual is null)
        {
            return;
        }

        _lastAnimationDistance = distance;
        var animation = visual.Compositor.CreateVector3DKeyFrameAnimation();
        animation.Target = "Offset";
        animation.Duration = TimeSpan.FromSeconds(distance / ScrollSpeed);
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
        animation.InsertKeyFrame(0, visual.Offset with { X = viewportWidth }, new LinearEasing());
        animation.InsertKeyFrame(1, visual.Offset with { X = -textWidth }, new LinearEasing());
        visual.StartAnimation("Offset", animation);
    }
}
