using System;
using System.IO;
using System.Linq;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.VisualTree;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using FluentAvalonia.UI.Controls;
using SystemTools.ConfigHandlers;
using SystemTools.Shared;
using SystemTools.Services;
using ClassIsland.Core.Abstractions;
using ClassIsland.Shared;

namespace SystemTools;

[HidePageTitle]
[SettingsPageInfo("systemtools.settings.main", "主设置", "\uE079", "\uE078")]
public partial class SystemToolsSettingsPage : SettingsPageBase
{
    public SystemToolsSettingsPage()
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

        // 初始化时更新下载按钮状态
        UpdateDownloadButtonStates();

        ViewModel.InitializeFeatureItems();
        ViewModel.RefreshFloatingTriggers();
        ViewModel.Settings.RestartPropertyChanged += OnRestartPropertyChanged;
        ViewModel.Settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    public SystemToolsSettingsViewModel ViewModel { get; }

    private void UpdateDownloadButtonStates()
    {
        ViewModel.IsFfmpegDownloadEnabled = !ViewModel.CheckFfmpegExists();
        ViewModel.IsFaceModelsDownloadEnabled = !ViewModel.CheckFaceModelsExists();
    }

    private void OnRestartPropertyChanged(object? sender, EventArgs e)
    {
        RequestRestart();
    }


    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainConfigData.ShowFloatingWindow)
            or nameof(MainConfigData.FloatingWindowScale))
        {
            IAppHost.GetService<FloatingWindowService>().UpdateWindowState();
        }
    }


    private void ButtonRestart_OnClick(object sender, RoutedEventArgs e)
    {
        RequestRestart();
    }


    private void OnFloatingFeatureToggleClick(object? sender, RoutedEventArgs e)
    {
        RequestRestart();
    }

    private async void OnFfmpegToggleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle) return;

        if (toggle.IsChecked == true)
        {
            if (!ViewModel.CheckFfmpegExists())
            {
                toggle.IsChecked = false;
                await ShowFfmpegNotFoundDialogAsync();
            }
            else
            {
                ViewModel.Settings.RestartPropertyChanged -= OnRestartPropertyChanged;
                ViewModel.Settings.EnableFfmpegFeatures = true;
                ViewModel.Settings.RestartPropertyChanged += OnRestartPropertyChanged;

                // 关闭功能时，允许重新下载（按钮启用状态由文件存在决定）
                ViewModel.IsFfmpegDownloadEnabled = !ViewModel.CheckFfmpegExists();

                RequestRestart();
            }
        }
        else
        {
            ViewModel.Settings.RestartPropertyChanged -= OnRestartPropertyChanged;
            ViewModel.Settings.EnableFfmpegFeatures = false;
            ViewModel.Settings.RestartPropertyChanged += OnRestartPropertyChanged;

            // 关闭功能时，允许重新下载（按钮启用状态由文件存在决定）
            ViewModel.IsFfmpegDownloadEnabled = !ViewModel.CheckFfmpegExists();

            RequestRestart();
        }
    }

    private async Task ShowFfmpegNotFoundDialogAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "提示",
            Content = "请您先下载本插件专用的ffmpeg模块！",
            PrimaryButtonText = "确定",
            DefaultButton = ContentDialogButton.Primary
        };

        await dialog.ShowAsync();
    }

    private async void OnFaceRecognitionToggleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle) return;

        if (toggle.IsChecked == true)
        {
            if (!ViewModel.CheckFaceModelsExists())
            {
                toggle.IsChecked = false;
                var dialog = new ContentDialog
                {
                    Title = "提示",
                    Content = "请您先下载人脸识别验证模型！",
                    PrimaryButtonText = "确定",
                    DefaultButton = ContentDialogButton.Primary
                };
                await dialog.ShowAsync();
            }
            else
            {
                RequestRestart();
            }
        }
        else
        {
            RequestRestart();
        }
    }

    private async void OnDownloadFaceModelsClick(object? sender, RoutedEventArgs e)
    {
        var success = await ViewModel.DownloadFaceModelsAsync(ShowErrorDialogAsync, ShowMd5ErrorDialogAsync);

        if (success)
        {
            // 下载成功后，根据文件存在状态更新按钮
            UpdateDownloadButtonStates();
        }
    }

    private async void OnDownloadFfmpegClick(object? sender, RoutedEventArgs e)
    {
        var success = await ViewModel.DownloadFfmpegAsync(ShowErrorDialogAsync, ShowMd5ErrorDialogAsync);

        if (success)
        {
            UpdateDownloadButtonStates();
        }
    }

    private async Task ShowErrorDialogAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "错误",
            Content = "下载出错，请重试！",
            PrimaryButtonText = "确定",
            DefaultButton = ContentDialogButton.Primary
        };
        await dialog.ShowAsync();
    }

    private async Task ShowMd5ErrorDialogAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "错误",
            Content = "下载文件MD5校验错误，请重新下载！",
            PrimaryButtonText = "确定",
            DefaultButton = ContentDialogButton.Primary
        };
        await dialog.ShowAsync();
    }

    private void OnManageFeaturesClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.FeatureDrawerContent = new object();
        ViewModel.IsFeatureDrawerOpen = true;
    }

    private void OnCloseDrawerClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.IsFeatureDrawerOpen = false;
    }

    private void OnSaveFromDrawerClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.SaveFeatureSettings();
        ViewModel.IsFeatureDrawerOpen = false;
        RequestRestart();
    }


    private void OnFloatingWindowConfigChanged(object? sender, RoutedEventArgs e)
    {
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
            //this.ShowWarningToast("至少需要保留 1 行。");
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