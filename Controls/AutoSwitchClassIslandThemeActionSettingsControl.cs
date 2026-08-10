using Avalonia.Controls;
using ClassIsland.Core.Abstractions.Controls;
using SystemTools.Settings;

namespace SystemTools.Controls;

public class AutoSwitchClassIslandThemeActionSettingsControl : ActionSettingsControlBase<AutoSwitchClassIslandThemeActionSettings>
{
    private ToggleSwitch _enableToggle;
    private CheckBox _notifyCheckBox;

    public AutoSwitchClassIslandThemeActionSettingsControl()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new(10) };

        panel.Children.Add(new TextBlock
        {
            Text = "自动切换 ClassIsland 主题",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            FontSize = 14
        });

        _enableToggle = new ToggleSwitch
        {
            OnContent = "开启",
            OffContent = "关闭"
        };
        _enableToggle.IsCheckedChanged += (s, e) => { Settings.Enable = _enableToggle.IsChecked ?? false; };
        panel.Children.Add(_enableToggle);

        panel.Children.Add(new TextBlock
        {
            Text = "设为“开启”时，触发行动将开启该功能；设为“关闭”时，触发行动将关闭该功能。",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Opacity = 0.7,
            FontSize = 12
        });

        _notifyCheckBox = new CheckBox { Content = "当执行时发出提醒" };
        _notifyCheckBox.IsCheckedChanged += (s, e) => { Settings.NotifyOnExecute = _notifyCheckBox.IsChecked ?? false; };
        panel.Children.Add(_notifyCheckBox);

        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _enableToggle.IsChecked = Settings.Enable;
        _notifyCheckBox.IsChecked = Settings.NotifyOnExecute;
    }
}