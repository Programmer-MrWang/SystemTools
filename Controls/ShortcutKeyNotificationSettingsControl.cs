using Avalonia.Controls;
using ClassIsland.Core.Abstractions.Controls;
using SystemTools.Settings;

namespace SystemTools.Controls;

public class ShortcutKeyNotificationSettingsControl : ActionSettingsControlBase<ShortcutKeyNotificationSettings>
{
    private CheckBox _notifyCheckBox;

    public ShortcutKeyNotificationSettingsControl()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new(10) };

        _notifyCheckBox = new CheckBox
        {
            Content = "当执行时发出提醒"
        };
        _notifyCheckBox.IsCheckedChanged += (s, e) =>
        {
            Settings.NotifyOnExecute = _notifyCheckBox.IsChecked ?? false;
        };
        panel.Children.Add(_notifyCheckBox);

        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _notifyCheckBox.IsChecked = Settings.NotifyOnExecute;
    }
}