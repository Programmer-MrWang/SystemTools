using Avalonia.Interactivity;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using SystemTools.ConfigHandlers;
using SystemTools.Services;
using SystemTools.Shared;

namespace SystemTools;

[SettingsPageInfo("systemtools.settings.more", "更多功能选项…", "\uE28E", "\uE28E", true)]
public partial class MoreFeaturesOptionsSettingsPage : SettingsPageBase
{
    public MainConfigData Config => GlobalConstants.MainConfig!.Data;

    public MoreFeaturesOptionsSettingsPage()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void AutoMatchThemeToggle_OnChanged(object? sender, RoutedEventArgs e)
    {
        var service = ClassIsland.Shared.IAppHost.GetService<AdaptiveThemeSyncService>();
        if (Config.AutoMatchMainBackgroundTheme)
        {
            service.RefreshNow();
        }

        GlobalConstants.MainConfig?.Save();
    }

    private void AutoHideMainWindowToggle_OnChanged(object? sender, RoutedEventArgs e)
    {
        var service = ClassIsland.Shared.IAppHost.GetService<MainWindowOcclusionAutoHideService>();
        if (Config.AutoHideMainWindowWhenOccluded)
        {
            service.RefreshNow();
        }

        GlobalConstants.MainConfig?.Save();
    }
}
