using Avalonia.Controls;
using Avalonia.Data;
using ClassIsland.Core.Abstractions.Controls;
using FluentAvalonia.UI.Controls;
using SystemTools.Settings;

namespace SystemTools.Controls;

public class ShowFloatingWindowSettingsControl : ActionSettingsControlBase<ShowFloatingWindowSettings>
{
    private CheckBox _notifyCheckBox;
    private readonly ToggleSwitch _toggleSwitch;

    public ShowFloatingWindowSettingsControl()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new(10) };

        _toggleSwitch = new ToggleSwitch
        {
            Content = "显示悬浮窗",
            IsChecked = true
        };

        panel.Children.Add(_toggleSwitch);

        _notifyCheckBox = new CheckBox { Content = "当执行时发出提醒" };
        _notifyCheckBox.IsCheckedChanged += (s, e) => { Settings.NotifyOnExecute = _notifyCheckBox.IsChecked ?? false; };
        panel.Children.Add(_notifyCheckBox);

        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _notifyCheckBox.IsChecked = Settings.NotifyOnExecute;

        _toggleSwitch.Bind(ToggleSwitch.IsCheckedProperty, new Binding(nameof(Settings.ShowFloatingWindow))
        {
            Source = Settings,
            Mode = BindingMode.TwoWay
        });
    }
}
