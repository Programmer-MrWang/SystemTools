using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ClassIsland.Core.Assists;
using ClassIsland.Core.Controls;

namespace SystemTools.Themes.NotchStyle;

public sealed class NotchMaterialControl : MainWindowBackgroundMaterialControl
{
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty ||
            change.Property == MainWindowStylesAssist.CornerRadiusProperty)
        {
            UpdateClip();
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var arranged = base.ArrangeOverride(finalSize);
        UpdateClip();
        return arranged;
    }

    private void UpdateClip()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        Clip = NotchShapeGeometry.Create(Bounds.Size, MainWindowStylesAssist.GetCornerRadius(this));
    }
}
