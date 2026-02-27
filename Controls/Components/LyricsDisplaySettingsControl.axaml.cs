﻿using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Helpers;
using FluentAvalonia.UI.Controls;
using Markdown.Avalonia;
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
                Title = "帮助",
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

    private async void OpenLyricifyLiteReadme()
    {
        try
        {
            var readmePath = Path.Combine(
                GlobalConstants.Information.PluginFolder,
                "Lyricify Lite - README.md");

            string content = File.Exists(readmePath) 
                ? File.ReadAllText(readmePath)
                : "**未找到文件**\n\n未找到 Lyricify Lite - README.md 文件，请检查插件目录。";

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var markdownViewer = new MarkdownScrollViewer
            {
                Markdown = content,
                Engine = MarkdownConvertHelper.Engine,
                MaxHeight = 370
            };
            
            var border = new Border
            {
                Child = markdownViewer,
                Padding = new Avalonia.Thickness(24, 0, 24, 0),
                MaxHeight = 377,
                Width = 550
            };

            var dialog = new ContentDialog
            {
                Title = "Lyricify Lite 适配帮助",
                Content = border,
                PrimaryButtonText = "了解",
                DefaultButton = ContentDialogButton.Primary
            };

            await dialog.ShowAsync(topLevel);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"显示帮助失败: {ex.Message}");
            ShowSimpleMessage("错误", $"无法显示帮助: {ex.Message}");
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
                PrimaryButtonText = "了解",
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