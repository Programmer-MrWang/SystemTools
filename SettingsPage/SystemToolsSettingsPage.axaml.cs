using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using FluentAvalonia.UI.Controls;
using SystemTools.ConfigHandlers;
using SystemTools.Shared;

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

        ViewModel = new SystemToolsSettingsViewModel(GlobalConstants.MainConfig);

        DataContext = this;
        InitializeComponent();

        if (ViewModel.CheckFfmpegExists()) ViewModel.IsDownloadButtonEnabled = false;

        ViewModel.InitializeFeatureItems();

        ViewModel.Settings.RestartPropertyChanged += OnRestartPropertyChanged;
    }

    public SystemToolsSettingsViewModel ViewModel { get; }

    private void OnRestartPropertyChanged(object? sender, EventArgs e)
    {
        RequestRestart();
    }

    private void ButtonRestart_OnClick(object sender, RoutedEventArgs e)
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

                ViewModel.IsDownloadButtonEnabled = false;

                RequestRestart();
            }
        }
        else
        {
            ViewModel.Settings.RestartPropertyChanged -= OnRestartPropertyChanged;
            ViewModel.Settings.EnableFfmpegFeatures = false;
            ViewModel.Settings.RestartPropertyChanged += OnRestartPropertyChanged;

            ViewModel.IsDownloadButtonEnabled = !ViewModel.CheckFfmpegExists();

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

    private async void OnDownloadFfmpegClick(object? sender, RoutedEventArgs e)
    {
        var success = await ViewModel.DownloadFfmpegAsync(ShowErrorDialogAsync, ShowMd5ErrorDialogAsync);

        if (success) ViewModel.IsDownloadButtonEnabled = false;
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
}