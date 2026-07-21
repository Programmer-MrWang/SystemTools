using Avalonia.Controls;
using Avalonia.Data;
using ClassIsland.Core.Abstractions.Controls;
using FluentAvalonia.UI.Controls;
using SystemTools.Settings;

namespace SystemTools.Controls;

public class ShowFloatingWindowSettingsControl : ActionSettingsControlBase<ShowFloatingWindowSettings>
{
    private readonly FAToggleSwitch _toggleSwitch;

    public ShowFloatingWindowSettingsControl()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new(10) };

        _toggleSwitch = new FAToggleSwitch
        {
            Content = "显示悬浮窗",
            IsChecked = true
        };

        panel.Children.Add(_toggleSwitch);

        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        _toggleSwitch.Bind(FAToggleSwitch.IsCheckedProperty, new Binding(nameof(Settings.ShowFloatingWindow))
        {
            Source = Settings,
            Mode = BindingMode.TwoWay
        });
    }
}
