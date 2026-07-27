using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ClassIsland.Core;

namespace SystemTools.Services;

public sealed class MainWindowAreaService
{
    private IReadOnlyList<Rectangle> _lastLayoutAreas = [];

    public IReadOnlyList<Rectangle> GetVisibleAreas()
    {
        return GetAreas(visibleOnly: true);
    }

    public IReadOnlyList<Rectangle> GetLayoutAreas()
    {
        var areas = GetAreas(visibleOnly: false);
        if (areas.Count > 0)
        {
            _lastLayoutAreas = areas;
        }

        return areas.Count > 0 ? areas : _lastLayoutAreas;
    }

    private static IReadOnlyList<Rectangle> GetAreas(bool visibleOnly)
    {
        if (AppBase.Current.MainWindow is not { IsVisible: true } window)
        {
            return [];
        }

        try
        {
            return window.GetVisualDescendants()
                .OfType<Grid>()
                .Where(grid => grid.Name == "PART_GridWrapper" &&
                               (!visibleOnly || grid.IsEffectivelyVisible))
                .Select(GetScreenRectangle)
                .Where(rectangle => rectangle.Width > 0 && rectangle.Height > 0)
                .ToList();
        }
        catch (InvalidOperationException)
        {
            return [];
        }
    }

    private static Rectangle GetScreenRectangle(Control control)
    {
        if (control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
        {
            return Rectangle.Empty;
        }

        var topLeft = control.PointToScreen(new Avalonia.Point(0, 0));
        var bottomRight = control.PointToScreen(new Avalonia.Point(control.Bounds.Width, control.Bounds.Height));
        var left = Math.Min(topLeft.X, bottomRight.X);
        var top = Math.Min(topLeft.Y, bottomRight.Y);
        var right = Math.Max(topLeft.X, bottomRight.X);
        var bottom = Math.Max(topLeft.Y, bottomRight.Y);

        return Rectangle.FromLTRB(left, top, right, bottom);
    }
}
