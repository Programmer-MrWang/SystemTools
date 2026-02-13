using System;
using Avalonia.Interactivity;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using CommunityToolkit.Mvvm.ComponentModel;
using SystemTools.ConfigHandlers;
using SystemTools.Shared;
using System.IO;

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
}

public partial class SystemToolsSettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private MainConfigData _settings;

    public SystemToolsSettingsViewModel(MainConfigHandler configHandler)
    {
        _settings = configHandler.Data;
    }
}