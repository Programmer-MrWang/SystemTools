using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Helpers;
using FluentAvalonia.UI.Controls;
using Markdown.Avalonia;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using SystemTools.Shared;

namespace SystemTools;

[HidePageTitle]
[SettingsPageInfo("systemtools.settings.about", "关于", "\uE9E4", "\uE9E4")]
public partial class AboutSettingsPage : SettingsPageBase
{
    public AboutSettingsViewModel ViewModel { get; }

    public AboutSettingsPage()
    {
        ViewModel = new AboutSettingsViewModel();
        DataContext = ViewModel;
        InitializeComponent();
        LoadPluginIcon();

        CheckAutoSwitchTab();
    }

    private void CheckAutoSwitchTab()
    {
        if (GlobalConstants.ShowChangelogOnOpen)
        {
            ViewModel.SelectedTabIndex = 2;
            GlobalConstants.ShowChangelogOnOpen = false;
        }
    }

    private void LoadPluginIcon()
    {
        try
        {
            var iconPath = Path.Combine(
                GlobalConstants.Information.PluginFolder,
                "icon-1.png");

            if (File.Exists(iconPath))
            {
                var bitmap = new Bitmap(iconPath);
                PluginIcon.Source = bitmap;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"加载图标失败: {ex.Message}");
        }
    }

    private async void OnLyricifyLiteHelpClick(object? sender, RoutedEventArgs e)
    {
        await ShowLyricifyLiteWarningAsync();
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
                Content = "     在使用适配 Lyricify Lite 的功能前，强烈建议您阅读相关使用方法！        \n\n     点击“不再提示”后您仍可以在本插件“关于”页面查看相关帮助。",
                PrimaryButtonText = "前往了解…",
                CloseButtonText = "以后再说",
                SecondaryButtonText = "关闭并不再显示",
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

public class AboutSettingsViewModel : INotifyPropertyChanged
{
    private string _currentMarkdownContent = string.Empty;
    private string _pluginVersion = "???";
    private int _selectedTabIndex = 0;

    public string CurrentMarkdownContent
    {
        get => _currentMarkdownContent;
        set
        {
            if (_currentMarkdownContent != value)
            {
                _currentMarkdownContent = value;
                OnPropertyChanged(nameof(CurrentMarkdownContent));
            }
        }
    }

    public string PluginVersion
    {
        get => _pluginVersion;
        set
        {
            if (_pluginVersion != value)
            {
                _pluginVersion = value;
                OnPropertyChanged(nameof(PluginVersion));
            }
        }
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (_selectedTabIndex != value)
            {
                _selectedTabIndex = value;
                OnPropertyChanged(nameof(SelectedTabIndex));
                OnPropertyChanged(nameof(IsHelpTab));
                OnPropertyChanged(nameof(IsNotHelpTab));
                LoadMarkdownContent();
            }
        }
    }

    public bool IsHelpTab => SelectedTabIndex == 0;
    public bool IsNotHelpTab => SelectedTabIndex != 0;

    private readonly string[] _markdownFiles =
    {
        "README.md",      // 帮助
        "README-1.md",        // 插件介绍-1
        "README-2.md"       // 更新日志
    };

    private readonly string[] _defaultContents =
    {
        "# 帮助",
        "# 插件介绍\n\n欢迎使用 SystemTools 插件！\n\n**未找到插件目录下的「README-1.md」文件。**",
        "# 更新日志\n\n**未找到插件目录下的「README-2.md」文件。**"
    };

    public AboutSettingsViewModel()
    {
        PluginVersion = GlobalConstants.Information.PluginVersion;
        LoadMarkdownContent();
    }

    private void LoadMarkdownContent()
    {
        try
        {
            if (SelectedTabIndex != 0)
            {
                var filePath = Path.Combine(
                    GlobalConstants.Information.PluginFolder,
                    _markdownFiles[SelectedTabIndex]);

                CurrentMarkdownContent = File.Exists(filePath)
                    ? File.ReadAllText(filePath)
                    : _defaultContents[SelectedTabIndex];
            }
            else
            {
                CurrentMarkdownContent = string.Empty;
            }

            Debug.WriteLine($"[SystemTools] 加载标签 {SelectedTabIndex}: {_markdownFiles[SelectedTabIndex]}");
        }
        catch (Exception ex)
        {
            CurrentMarkdownContent = $"# 错误\n\n加载文件时出错：{ex.Message}";
            Debug.WriteLine($"[SystemTools] 加载失败: {ex.Message}");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}