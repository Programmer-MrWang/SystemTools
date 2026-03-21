using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using SystemTools.ConfigHandlers;
using SystemTools.Services;
using SystemTools.Shared;

namespace SystemTools;

[HidePageTitle]
[SettingsPageInfo("systemtools.settings.floating", "悬浮窗编辑", "\uEA37", "\uEA37")]
public partial class FloatingWindowEditorSettingsPage : SettingsPageBase
{
    public FloatingWindowEditorSettingsPage()
    {
        if (GlobalConstants.MainConfig == null)
            GlobalConstants.MainConfig = new MainConfigHandler(GlobalConstants.PluginConfigFolder
                                                               ?? Path.Combine(
                                                                   Environment.GetFolderPath(Environment.SpecialFolder
                                                                       .LocalApplicationData), "ClassIsland", "Plugins",
                                                                   "SystemTools"));

        ViewModel = new SystemToolsSettingsViewModel(GlobalConstants.MainConfig,
            IAppHost.GetService<FloatingWindowService>());
        DataContext = this;
        InitializeComponent();

        ViewModel.RefreshFloatingTriggers();
        ViewModel.Settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    public SystemToolsSettingsViewModel ViewModel { get; }
    private bool _isDisposed;

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_isDisposed)
        {
            return;
        }

        ViewModel.Settings.PropertyChanged -= OnSettingsPropertyChanged;
        ViewModel.Dispose();
        _isDisposed = true;
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainConfigData.ShowFloatingWindow)
            or nameof(MainConfigData.FloatingWindowScale)
            or nameof(MainConfigData.FloatingWindowIconSize)
            or nameof(MainConfigData.FloatingWindowTextSize)
            or nameof(MainConfigData.FloatingWindowOpacity)
            or nameof(MainConfigData.FloatingWindowTheme)
            or nameof(MainConfigData.FloatingWindowShadowEnabled)
            or nameof(MainConfigData.FloatingWindowLayer)
            or nameof(MainConfigData.FloatingWindowLayerRecheckMode))
        {
            IAppHost.GetService<FloatingWindowService>().UpdateWindowState();
        }
    }

    private void OnFloatingWindowConfigChanged(object? sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasFloatingTriggerEntries)
        {
            ViewModel.Settings.ShowFloatingWindow = false;
        }

        ViewModel.RefreshFloatingTriggers();
        IAppHost.GetService<FloatingWindowService>().UpdateWindowState();
    }

    private Point? _floatingDragStartPoint;
    private Border? _floatingDragSourceBorder;

    private void OnAddFloatingTriggerRowClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.AddFloatingTriggerRow();
    }

    private void OnRemoveFloatingTriggerRowClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: FloatingTriggerRow row })
        {
            return;
        }

        if (ViewModel.FloatingTriggerRows.Count <= 1)
        {
            return;
        }

        _ = ViewModel.RemoveFloatingTriggerRow(row);
    }

    private void OnFloatingTriggerItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || !e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _floatingDragSourceBorder = border;
        _floatingDragStartPoint = e.GetPosition(border);
        e.Handled = e.Pointer.Type is PointerType.Touch or PointerType.Pen;
    }

    private void OnFloatingTriggerItemPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _floatingDragSourceBorder = null;
        _floatingDragStartPoint = null;
    }

    private async void OnFloatingTriggerItemPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Border border || _floatingDragSourceBorder != border || _floatingDragStartPoint == null)
        {
            return;
        }

        if (!e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var now = e.GetPosition(border);
        if (Math.Abs(now.X - _floatingDragStartPoint.Value.X) + Math.Abs(now.Y - _floatingDragStartPoint.Value.Y) < 4)
        {
            return;
        }

        if (border.Tag is not string buttonId || string.IsNullOrWhiteSpace(buttonId))
        {
            return;
        }

        var data = new DataObject();
        data.Set("FloatingTriggerButtonId", buttonId);

        _floatingDragSourceBorder = null;
        _floatingDragStartPoint = null;
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
        e.Handled = e.Pointer.Type is PointerType.Touch or PointerType.Pen;
    }

    private static bool TryGetDragButtonId(DragEventArgs e, out string buttonId)
    {
        buttonId = string.Empty;
        if (!e.Data.Contains("FloatingTriggerButtonId"))
        {
            return false;
        }

        buttonId = e.Data.Get("FloatingTriggerButtonId") as string ?? string.Empty;
        return !string.IsNullOrWhiteSpace(buttonId);
    }

    private int GetRowIndexFromControl(Control? control)
    {
        var current = control;
        while (current != null)
        {
            if (current.DataContext is FloatingTriggerRow row)
            {
                return ViewModel.FloatingTriggerRows.IndexOf(row);
            }

            current = current.GetVisualParent() as Control;
        }

        return -1;
    }

    private int GetRowInsertIndex(Control sender, FloatingTriggerRow row, DragEventArgs e)
    {
        if (row.Buttons.Count == 0)
        {
            return 0;
        }

        var pointer = e.GetPosition(sender);
        var itemBorders = sender.GetVisualDescendants()
            .OfType<Border>()
            .Where(x => x.DataContext is FloatingTriggerItem)
            .OrderBy(x => x.TranslatePoint(new Point(0, 0), sender)?.X ?? double.MaxValue)
            .ToList();

        for (var i = 0; i < itemBorders.Count; i++)
        {
            var topLeft = itemBorders[i].TranslatePoint(new Point(0, 0), sender);
            if (topLeft == null)
            {
                continue;
            }

            var center = topLeft.Value.X + itemBorders[i].Bounds.Width / 2;
            if (pointer.X <= center)
            {
                return i;
            }
        }

        return row.Buttons.Count;
    }

    private void OnFloatingTriggerRowDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = TryGetDragButtonId(e, out _) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFloatingTriggerRowDrop(object? sender, DragEventArgs e)
    {
        if (!TryGetDragButtonId(e, out var buttonId) || sender is not Control senderControl)
        {
            return;
        }

        var rowIndex = GetRowIndexFromControl(senderControl);
        if (rowIndex < 0)
        {
            return;
        }

        var row = ViewModel.FloatingTriggerRows[rowIndex];
        var insertIndex = GetRowInsertIndex(senderControl, row, e);
        ViewModel.MoveFloatingTrigger(buttonId, rowIndex, insertIndex);
    }

    private void OnFloatingTriggerItemDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = TryGetDragButtonId(e, out _) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFloatingTriggerItemDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not FloatingTriggerItem targetItem)
        {
            return;
        }

        if (!TryGetDragButtonId(e, out var buttonId))
        {
            return;
        }

        var rowIndex = GetRowIndexFromControl(border);
        if (rowIndex < 0)
        {
            return;
        }

        var row = ViewModel.FloatingTriggerRows[rowIndex];
        var targetIndex = row.Buttons.IndexOf(targetItem);
        if (targetIndex < 0)
        {
            return;
        }

        var pos = e.GetPosition(border);
        if (pos.X > border.Bounds.Width / 2)
        {
            targetIndex += 1;
        }

        ViewModel.MoveFloatingTrigger(buttonId, rowIndex, targetIndex);
    }
}