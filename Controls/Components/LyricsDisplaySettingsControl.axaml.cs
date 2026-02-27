using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using FluentAvalonia.UI.Controls;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using SystemTools.Models.ComponentSettings;
using SystemTools.Shared;

namespace SystemTools.Controls.Components;

public partial class LyricsDisplaySettingsControl : ComponentBase<LyricsDisplaySettings>
{
    public LyricsDisplaySettingsControl()
    {
        InitializeComponent();
    }

    private void MusicSoftwareComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Settings.SelectedMusicSoftware == MusicSoftware.LyricifyLite)
        {
            if (GlobalConstants.MainConfig?.Data.LyricifyLiteWarningDismissed == true)
                return;

            Dispatcher.UIThread.Post(async () => await ShowLyricifyLiteWarningAsync(), DispatcherPriority.Background);
        }
    }

    private async Task ShowLyricifyLiteWarningAsync()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var dialog = new ContentDialog
            {
                Title = "提示",
                Content = "在使用适配 Lyricify Lite 的功能前，强烈建议您阅读相关使用方法！\n 点击“不再提示”后您仍可以在本插件“关于”页面查看相关帮助。",
                CloseButtonText = "关闭",
                SecondaryButtonText = "已了解，不再提示",
                PrimaryButtonText = "前往了解…",
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync(topLevel);

            if (result == ContentDialogResult.Secondary && GlobalConstants.MainConfig != null)
            {
                GlobalConstants.MainConfig.Data.LyricifyLiteWarningDismissed = true;
                GlobalConstants.MainConfig.Save();
            }
            else if (result == ContentDialogResult.Primary)
            {
                OpenLyricifyLiteReadme();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"显示对话框失败: {ex.Message}");
        }
    }

    private void OpenLyricifyLiteReadme()
    {
        try
        {
            var readmePath = Path.Combine(
                GlobalConstants.Information.PluginFolder,
                "Lyricify Lite - README.md");

            if (File.Exists(readmePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = readmePath,
                    UseShellExecute = true
                });
            }
            else
            {
                ShowSimpleMessage("错误", "未找到 Lyricify Lite - README.md 文件，请检查插件目录。");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"打开文件失败: {ex.Message}");
            ShowSimpleMessage("错误", $"无法打开文件: {ex.Message}");
        }
    }

    private async void ShowSimpleMessage(string title, string message)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                PrimaryButtonText = "确定",
                DefaultButton = ContentDialogButton.Primary
            };

            await dialog.ShowAsync(topLevel);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"显示消息失败: {ex.Message}");
        }
    }
}