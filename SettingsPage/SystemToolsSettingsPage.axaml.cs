using System;
using System.IO;
using System.Linq;
using System.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using ClassIsland.Core.Abstractions.Services;

namespace SystemTools;

[HidePageTitle]
[SettingsPageInfo("systemtools.settings.main", "主设置", "", "")]
public partial class SystemToolsSettingsPage : SettingsPageBase
{
    public MainConfigData Config => GlobalConstants.MainConfig!.Data;
    public ObservableCollection<string> AvailableAiModels { get; } = [];
    public IReadOnlyList<SpeechRecognitionDownloadOption> SpeechRecognitionModels =>
        SystemToolsSettingsViewModel.SpeechRecognitionModels;

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

        if (!string.IsNullOrWhiteSpace(Config.AiModel))
        {
            AvailableAiModels.Add(Config.AiModel);
        }
    }

    public SystemToolsSettingsViewModel ViewModel { get; }

    private void UpdateDownloadButtonStates()
    {
        ViewModel.RefreshDownloadButtonStates();
    }

    private void OnRestartPropertyChanged(object? sender, EventArgs e)
    {
        RequestRestart();
    }


    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 主设置页面不再直接监听悬浮窗属性变化，由 FloatingWindowEditorSettingsPage 处理
    }


    private void ButtonRestart_OnClick(object sender, RoutedEventArgs e)
    {
        RequestRestart();
    }


    private void OnFloatingFeatureToggleClick(object? sender, RoutedEventArgs e)
    {
        RequestRestart();
    }

    private async void AiServiceToggle_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggleSwitch)
        {
            return;
        }

        if (toggleSwitch.IsChecked != true)
        {
            Config.EnableAiService = false;
            GlobalConstants.MainConfig?.Save();
            RestartClassIsland();
            return;
        }

        toggleSwitch.IsEnabled = false;
        var accepted = await ShowAiServiceAgreementAsync();
        toggleSwitch.IsEnabled = true;
        if (!accepted)
        {
            toggleSwitch.IsChecked = false;
            return;
        }

        Config.EnableAiService = true;
        GlobalConstants.MainConfig?.Save();
        RestartClassIsland();
    }

    private async void VoiceWakeAiToggle_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggleSwitch)
        {
            return;
        }

        if (toggleSwitch.IsChecked != true)
        {
            Config.EnableVoiceWakeAi = false;
            GlobalConstants.MainConfig?.Save();
            IAppHost.TryGetService<AiVoiceConversationService>()?.ApplyConfig();
            return;
        }

        if (!Config.EnableAiService)
        {
            toggleSwitch.IsChecked = false;
            return;
        }

        if (string.IsNullOrWhiteSpace(Config.AiModel))
        {
            toggleSwitch.IsChecked = false;
            await ShowAiMessageAsync("无法启用语音唤醒", "请先在上方获取并选择一个 AI 模型。");
            return;
        }

        var dependencyCheck = DependencyPaths.CheckSpeechRecognitionDependencies();
        if (!dependencyCheck.IsAvailable)
        {
            toggleSwitch.IsChecked = false;
            await ShowAiMessageAsync("无法启用语音唤醒", dependencyCheck.Message);
            return;
        }

        toggleSwitch.IsEnabled = false;
        try
        {
            Config.EnableVoiceWakeAi = true;
            GlobalConstants.MainConfig?.Save();
            var service = IAppHost.TryGetService<AiVoiceConversationService>();
            if (service == null)
            {
                Config.EnableVoiceWakeAi = false;
                GlobalConstants.MainConfig?.Save();
                toggleSwitch.IsChecked = false;
                await ShowAiMessageAsync("需要重启 ClassIsland", "AI 服务尚未在本次运行中加载，请重启 ClassIsland 后再启用语音唤醒。");
                return;
            }

            service.ApplyConfig();
            if (!service.IsWakeWordEnabled)
            {
                Config.EnableVoiceWakeAi = false;
                GlobalConstants.MainConfig?.Save();
                toggleSwitch.IsChecked = false;
                await ShowAiMessageAsync("无法启用语音唤醒", service.LastError ?? "语音唤醒服务未能启动。");
            }
        }
        finally
        {
            toggleSwitch.IsEnabled = true;
        }
    }

    private void CheckCurrentVoskModelButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        button.Content = "重新检查";
        try
        {
            var modelDirectory = DependencyPaths.FindSpeechRecognitionModelDirectory();
            if (modelDirectory is null)
            {
                CurrentVoskModelText.Text = "未找到可用的语音识别模型。";
            }
            else
            {
                var model = DependencyPaths.GetSpeechRecognitionModelInfo(modelDirectory);
                CurrentVoskModelText.Text = string.IsNullOrWhiteSpace(model?.Name)
                    ? "已找到当前模型，但 copyright.txt 中未提供模型名称。"
                    : $"当前正在使用 {model.Name} 模型";
            }
        }
        catch (Exception ex)
        {
            CurrentVoskModelText.Text = $"检查当前模型失败：{ex.Message}";
        }

        CurrentVoskModelText.IsVisible = true;
    }

    private async Task<bool> ShowAiServiceAgreementAsync()
    {
        var agreementCheckBox = new CheckBox
        {
            Content = new TextBlock
            {
                Text = "我已阅读本协议，自愿承担使用AI带来的不确定风险",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                MaxWidth = 520
            }
        };
        var dialog = new FAContentDialog
        {
            Title = "AI 服务使用协议",
            Content = new StackPanel
            {
                Spacing = 16,
                MaxWidth = 540,
                Children =
                {
                    new TextBlock
                    {
                        Text = "此“AI 服务”是由SystemTools插件提供的外接 API Key 的AI辅助功能，与ClassIsland软件无关；\n" +
                               "AI的回复和相关服务由对应提供商提供，与本插件及开发者无关；\n" +
                               "使用课表问答或修改功能时，当前档案中的课表、时间表、科目、任课教师及扩展配置会发送给您配置的 AI 服务提供商；\n" +
                               "须知应当正确使用AI，合理规避不确定性风险，明辨AI提供的相关回复。",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    agreementCheckBox
                }
            },
            CloseButtonText = "取消",
            PrimaryButtonText = "确定",
            DefaultButton = FAContentDialogButton.Close,
            IsPrimaryButtonEnabled = false
        };

        agreementCheckBox.IsCheckedChanged += (_, _) =>
            dialog.IsPrimaryButtonEnabled = agreementCheckBox.IsChecked == true;

        return await dialog.ShowAsync(TopLevel.GetTopLevel(this)) == FAContentDialogResult.Primary;
    }

    private async void GetAiModelsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var originalContent = button.Content;
        button.IsEnabled = false;
        button.Content = "正在获取...";

        try
        {
            var service = IAppHost.GetService<IOpenAiCompatibleService>();
            var models = await service.GetModelsAsync();
            if (models.Count == 0)
            {
                await ShowAiMessageAsync("未找到模型", "供应商返回了空的模型列表。");
                return;
            }

            var previousModel = Config.AiModel;
            AvailableAiModels.Clear();
            foreach (var model in models)
            {
                AvailableAiModels.Add(model);
            }

            Config.AiModel = models.Contains(previousModel, StringComparer.Ordinal)
                ? previousModel
                : models[0];
            GlobalConstants.MainConfig?.Save();

            await ShowAiMessageAsync("获取成功", $"已获取 {models.Count} 个可用模型。");
        }
        catch (Exception ex)
        {
            await ShowAiMessageAsync("获取模型失败", ex.Message);
        }
        finally
        {
            button.Content = originalContent;
            button.IsEnabled = Config.EnableAiService;
        }
    }

    private static async Task ShowAiMessageAsync(string title, string message)
    {
        var dialog = new FAContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = "确定",
            DefaultButton = FAContentDialogButton.Primary
        };

        await dialog.ShowAsync();
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
                UpdateDownloadButtonStates();

                RequestRestart();
            }
        }
        else
        {
            ViewModel.Settings.RestartPropertyChanged -= OnRestartPropertyChanged;
            ViewModel.Settings.EnableFfmpegFeatures = false;
            ViewModel.Settings.RestartPropertyChanged += OnRestartPropertyChanged;

            // 关闭功能时，允许重新下载（按钮启用状态由文件存在决定）
            UpdateDownloadButtonStates();

            RequestRestart();
        }
    }

    private async Task ShowFfmpegNotFoundDialogAsync()
    {
        var dialog = new FAContentDialog
        {
            Title = "提示",
            Content = "请您先下载本插件专用的ffmpeg模块！",
            PrimaryButtonText = "确定",
            DefaultButton = FAContentDialogButton.Primary
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
                var dialog = new FAContentDialog
                {
                    Title = "提示",
                    Content = "请您先下载人脸识别验证模型及运行时依赖！",
                    PrimaryButtonText = "确定",
                    DefaultButton = FAContentDialogButton.Primary
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
        var dialog = new FAContentDialog
        {
            Title = "错误",
            Content = "下载出错，请重试！",
            PrimaryButtonText = "确定",
            DefaultButton = FAContentDialogButton.Primary
        };
        await dialog.ShowAsync();
    }

    private async Task ShowMd5ErrorDialogAsync()
    {
        var dialog = new FAContentDialog
        {
            Title = "错误",
            Content = "下载文件MD5校验错误，请重新下载！",
            PrimaryButtonText = "确定",
            DefaultButton = FAContentDialogButton.Primary
        };
        await dialog.ShowAsync();
    }

    private void OnManageFeaturesClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.FeatureDrawerContent = new object();
        ViewModel.IsFeatureDrawerOpen = true;
    }

    private void OnOpenMoreFeaturesClick(object? sender, RoutedEventArgs e)
    {
        IAppHost.GetService<IUriNavigationService>()
            .NavigateWrapped(new Uri("classisland://app/settings/systemtools.settings.more?ci_keepHistory=true"));
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
    private PointerPressedEventArgs? _floatingDragPressedArgs;
    private static readonly DataFormat<string> FloatingTriggerButtonIdFormat =
        DataFormat.CreateStringApplicationFormat("FloatingTriggerButtonId");

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
        _floatingDragPressedArgs = e;
        e.Handled = e.Pointer.Type is PointerType.Touch or PointerType.Pen;
    }

    private void OnFloatingTriggerItemPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _floatingDragSourceBorder = null;
        _floatingDragStartPoint = null;
        _floatingDragPressedArgs = null;
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

        if (_floatingDragPressedArgs == null)
        {
            return;
        }

        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(FloatingTriggerButtonIdFormat, buttonId));

        _floatingDragSourceBorder = null;
        _floatingDragStartPoint = null;
        await DragDrop.DoDragDropAsync(_floatingDragPressedArgs, data, DragDropEffects.Move);
        _floatingDragPressedArgs = null;
        e.Handled = e.Pointer.Type is PointerType.Touch or PointerType.Pen;
    }

    private static bool TryGetDragButtonId(DragEventArgs e, out string buttonId)
    {
        buttonId = e.DataTransfer.TryGetValue(FloatingTriggerButtonIdFormat) ?? string.Empty;
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
        var itemsControl = sender as ItemsControl
                           ?? sender.GetVisualDescendants()
                               .OfType<ItemsControl>()
                               .FirstOrDefault(x => ReferenceEquals(x.ItemsSource, row.Buttons));
        if (itemsControl == null)
        {
            return row.Buttons.Count;
        }

        for (var i = 0; i < row.Buttons.Count; i++)
        {
            var container = itemsControl.ContainerFromIndex(i);
            var topLeft = container?.TranslatePoint(new Point(0, 0), sender);
            if (topLeft == null)
            {
                continue;
            }

            var center = topLeft.Value.X + container!.Bounds.Width / 2;
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

    private static void RestartClassIsland()
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Environment.ProcessPath?.Replace(".dll", ".exe"),
                UseShellExecute = true
            };

            startInfo.ArgumentList.Add("-m");

            var args = Environment.GetCommandLineArgs().ToList();
            args.RemoveAt(0);
            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            System.Diagnostics.Process.Start(startInfo);
            AppBase.Current.Stop();
        }
        catch
        {
            // Silently fail if restart is not possible.
        }
    }

    private async void OnSpeechRecognitionModelActionClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.IsSelectedSpeechRecognitionModelInstalled())
        {
            await ViewModel.DeleteSelectedSpeechRecognitionModelAsync(ShowErrorDialogAsync);
        }
        else
        {
            await ViewModel.DownloadSpeechRecognitionModelAsync(ShowErrorDialogAsync, ShowMd5ErrorDialogAsync);
        }

        UpdateDownloadButtonStates();
    }

    private async void OnDownloadVoskWorkerClick(object? sender, RoutedEventArgs e)
    {
        await ViewModel.DownloadVoskWorkerAsync(ShowErrorDialogAsync, ShowMd5ErrorDialogAsync);
        UpdateDownloadButtonStates();
    }
}
