using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ClassIsland.Core.Assists;

namespace SystemTools.Themes.NotchStyle;

public sealed class NotchClipControl : Decorator
{
    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);
        Clip = NotchShapeGeometry.Create(arranged, MainWindowStylesAssist.GetCornerRadius(this));
        return arranged;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty ||
            change.Property == MainWindowStylesAssist.CornerRadiusProperty)
        {
            Clip = NotchShapeGeometry.Create(Bounds.Size, MainWindowStylesAssist.GetCornerRadius(this));
        }
    }
}
