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
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Shared;
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
    }
    
    private void UriNavigationCommands_OnClick(object sender, RoutedEventArgs e)
    {
        var url = e.Source switch
        {
            SettingsExpanderItem s => s.CommandParameter?.ToString(),
            Button s => s.CommandParameter?.ToString(),
            _ => "classisland://app/test/"
        };
        if (url != null)
        {
            IAppHost.TryGetService<IUriNavigationService>()?.NavigateWrapped(new Uri(url));
        }
    }

    private void LoadPluginIcon()
    {
        try
        {
            var iconPath = Path.Combine(
                GlobalConstants.Information.PluginFolder,
                "Assets",
                "icon.png");

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
                "Docs",
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

    private const string HelpFileName = "README.md";

    public AboutSettingsViewModel()
    {
        PluginVersion = GlobalConstants.Information.PluginVersion;
        LoadMarkdownContent();
    }

    private void LoadMarkdownContent()
    {
        try
        {
            var filePath = Path.Combine(
                GlobalConstants.Information.PluginFolder,
                HelpFileName);

            CurrentMarkdownContent = File.Exists(filePath)
                ? File.ReadAllText(filePath)
                : "# 帮助\n\n未找到 README.md 文件。";

            Debug.WriteLine($"[SystemTools] 加载帮助文档: {HelpFileName}");
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