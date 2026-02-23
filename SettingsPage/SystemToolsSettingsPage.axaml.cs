using Avalonia.Interactivity;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.UI.Controls;
using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using SystemTools.ConfigHandlers;
using SystemTools.Shared;

namespace SystemTools;

[HidePageTitle]
[SettingsPageInfo("systemtools.settings.main", "主设置", "\uE079", "\uE078")]
public partial class SystemToolsSettingsPage : SettingsPageBase
{
    public SystemToolsSettingsViewModel ViewModel { get; }

    public SystemToolsSettingsPage()
    {
        if (GlobalConstants.MainConfig == null)
        {
            GlobalConstants.MainConfig = new MainConfigHandler(GlobalConstants.PluginConfigFolder
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClassIsland", "Plugins", "SystemTools"));
        }

        ViewModel = new SystemToolsSettingsViewModel(GlobalConstants.MainConfig);

        DataContext = this;
        InitializeComponent();

        if (ViewModel.CheckFfmpegExists())
        {
            ViewModel.IsDownloadButtonEnabled = false;
        }

        ViewModel.Settings.RestartPropertyChanged += OnRestartPropertyChanged;
    }

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
        if (sender is not Avalonia.Controls.ToggleSwitch toggle) return;

        if (toggle.IsChecked == true)
        {
            if (!ViewModel.CheckFfmpegExists())
            {
                toggle.IsChecked = false;
                await ShowFfmpegNotFoundDialogAsync();
                return;
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

        if (success)
        {
            ViewModel.IsDownloadButtonEnabled = false;
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
}

public partial class SystemToolsSettingsViewModel(MainConfigHandler configHandler) : ObservableObject
{
    [ObservableProperty]
    private MainConfigData _settings = configHandler.Data;

    [ObservableProperty]
    private bool _isDownloadButtonEnabled = true;

    [ObservableProperty]
    private bool _showDownloadProgress = false;

    [ObservableProperty]
    private double _downloadProgress = 0;

    [ObservableProperty]
    private string _downloadStatusText = string.Empty;

    private const string DownloadUrl = "https://livefile.xesimg.com/programme/python_assets/f94fcfa40c9de41d6df09566a51e3130.exe";
    private const string ExpectedMd5 = "f94fcfa40c9de41d6df09566a51e3130";
    private const string TempFileName = "f94fcfa40c9de41d6df09566a51e3130.exe";
    private const string TargetFileName = "ffmpeg.exe";

    public bool CheckFfmpegExists()
    {
        try
        {
            var ffmpegPath = Path.Combine(
                GlobalConstants.Information.PluginFolder,
                TargetFileName);
            return File.Exists(ffmpegPath);
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DownloadFfmpegAsync(Func<Task> onError, Func<Task> onMd5Error)
    {
        if (!IsDownloadButtonEnabled) return false;

        IsDownloadButtonEnabled = false;
        ShowDownloadProgress = true;
        DownloadProgress = 0;
        DownloadStatusText = "正在下载 - 0%";

        var tempPath = Path.Combine(GlobalConstants.Information.PluginFolder, TempFileName);
        var targetPath = Path.Combine(GlobalConstants.Information.PluginFolder, TargetFileName);

        try
        {
            using var httpClient = new HttpClient();
            using var response = await httpClient.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var downloadedBytes = 0L;

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[4 * 1024 * 1024];
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                downloadedBytes += bytesRead;

                if (totalBytes > 0)
                {
                    var progress = (double)downloadedBytes / totalBytes * 100;
                    await UpdateProgressAsync(progress);
                }
            }

            fileStream.Close();
            await Task.Delay(500);
            await UpdateStatusAsync("正在校验MD5…");

            var actualMd5 = await CalculateMd5Async(tempPath);
            if (!string.Equals(actualMd5, ExpectedMd5, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(tempPath);
                await onMd5Error();
                IsDownloadButtonEnabled = true;
                return false;
            }

            await Task.Delay(500);
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
            File.Move(tempPath, targetPath);
            await Task.Delay(500);
            ShowDownloadProgress = false;

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SystemTools] 下载失败: {ex.Message}");

            if (File.Exists(tempPath))
            {
                await Task.Delay(2000);
                File.Delete(tempPath);
            }

            await onError();
            IsDownloadButtonEnabled = true;
            return false;
        }
        finally
        {
            if (!ShowDownloadProgress)
            {
                DownloadProgress = 0;
                DownloadStatusText = string.Empty;
            }
        }
    }

    private async Task UpdateProgressAsync(double progress)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            DownloadProgress = progress;
            DownloadStatusText = $"正在下载 - {progress:F0}%";
        });
    }

    private async Task UpdateStatusAsync(string status)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            DownloadStatusText = status;
        });
    }

    private static async Task<string> CalculateMd5Async(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await MD5.HashDataAsync(stream);
        return Convert.ToHexString(hash);
    }
}