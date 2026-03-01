using Avalonia.Media.Imaging;
using FluentAvalonia.UI.Controls;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using System;
using System.ComponentModel;
using System.IO;
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
        //InitializeAnnouncementBar();
    }

    /*private void InitializeAnnouncementBar()
    {
        AnnouncementBar.IsOpen = GlobalConstants.StaticAnnouncement != 
                                GlobalConstants.MainConfig?.Data.LastAcceptedAnnouncement;
    }

    private void AnnouncementBar_OnClosed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        if (GlobalConstants.MainConfig?.Data != null)
        {
            GlobalConstants.MainConfig.Data.LastAcceptedAnnouncement = GlobalConstants.StaticAnnouncement;
            GlobalConstants.MainConfig.Save();
        }
    }*/

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
            System.Diagnostics.Debug.WriteLine($"加载图标失败: {ex.Message}");
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
                LoadMarkdownContent();
            }
        }
    }

    //public string StaticAnnouncement => GlobalConstants.StaticAnnouncement;
    //public string QqGroupUrl => GlobalConstants.QqGroupUrl;

    private readonly string[] _markdownFiles =
    {
        "README-1.md",
        "README.md",
        "README-2.md"
    };

    private readonly string[] _defaultContents =
    {
        "# 帮助\n\n欢迎使用 SystemTools 插件！\n\n**未找到插件目录下的「README-1.md」文件。**",
        "# 插件介绍\n\n欢迎使用 SystemTools 插件！\n\n**未找到插件目录下的「README.md」文件。**",
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
            var filePath = Path.Combine(
                GlobalConstants.Information.PluginFolder,
                _markdownFiles[SelectedTabIndex]);

            CurrentMarkdownContent = File.Exists(filePath)
                ? File.ReadAllText(filePath)
                : _defaultContents[SelectedTabIndex];

            System.Diagnostics.Debug.WriteLine($"[SystemTools] 加载标签 {SelectedTabIndex}: {filePath}");
        }
        catch (Exception ex)
        {
            CurrentMarkdownContent = $"# 错误\n\n加载文件时出错：{ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[SystemTools] 加载失败: {ex.Message}");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}