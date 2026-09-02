using System;
using Avalonia;
using Avalonia.Media;

namespace SystemTools.Themes.NotchStyle;

internal static class NotchShapeGeometry
{
    public static StreamGeometry Create(Size size, double bottomRadius)
    {
        var width = Math.Max(0, size.Width);
        var height = Math.Max(0, size.Height);
        var topInset = Math.Min(15, width / 3);
        var topCurveDepth = Math.Min(40, height * 0.55);
        var bodyWidth = Math.Max(0, width - topInset * 2);
        var lowerRadius = Math.Clamp(bottomRadius, 0, Math.Min(bodyWidth / 2, height));

        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        context.BeginFigure(new Point(0, 0), true);
        context.LineTo(new Point(width, 0));
        context.CubicBezierTo(
            new Point(width - topInset * 0.45, 0),
            new Point(width - topInset, topCurveDepth * 0.35),
            new Point(width - topInset, topCurveDepth),
            true);
        context.LineTo(new Point(width - topInset, height - lowerRadius));
        context.CubicBezierTo(
            new Point(width - topInset, height - lowerRadius + lowerRadius * 0.55),
            new Point(width - topInset - lowerRadius * 0.55, height),
            new Point(width - topInset - lowerRadius, height),
            true);
        context.LineTo(new Point(topInset + lowerRadius, height));
        context.CubicBezierTo(
            new Point(topInset + lowerRadius * 0.55, height),
            new Point(topInset, height - lowerRadius * 0.55),
            new Point(topInset, height - lowerRadius),
            true);
        context.LineTo(new Point(topInset, topCurveDepth));
        context.CubicBezierTo(
            new Point(topInset, topCurveDepth * 0.35),
            new Point(topInset * 0.45, 0),
            new Point(0, 0),
            true);
        context.EndFigure(true);
        return geometry;
    }
}
