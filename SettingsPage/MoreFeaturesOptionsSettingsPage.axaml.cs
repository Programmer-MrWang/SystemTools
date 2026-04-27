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

    private void AutoOpenUsbToggle_OnChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is Avalonia.Controls.ToggleSwitch toggleSwitch)
        {
            Config.AutoOpenUsbDriveOnInsert = toggleSwitch.IsChecked == true;
        }

        var service = ClassIsland.Shared.IAppHost.GetService<UsbAutoPlayService>();
        service.ApplyConfig();
        GlobalConstants.MainConfig?.Save();
    }
}
