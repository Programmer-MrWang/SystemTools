using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ClassIsland.Core.Assists;

namespace SystemTools.Themes.NotchStyle;

public sealed class NotchFrameControl : Control
{
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<NotchFrameControl, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<NotchFrameControl, double>(nameof(StrokeThickness), 1);

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty ||
            change.Property == StrokeProperty ||
            change.Property == StrokeThicknessProperty ||
            change.Property == MainWindowStylesAssist.CornerRadiusProperty)
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0 || Stroke is not { } stroke)
        {
            return;
        }

        var geometry = NotchShapeGeometry.Create(Bounds.Size, MainWindowStylesAssist.GetCornerRadius(this));
        context.DrawGeometry(null, new Pen(stroke, StrokeThickness), geometry);
    }
}
