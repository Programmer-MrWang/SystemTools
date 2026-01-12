/*using Avalonia.Controls;
using Avalonia.Data;
using ClassIsland.Core.Abstractions.Controls;
using SystemTools.Rules;

namespace SystemTools.Controls;

public class ScreenColorRuleSettingsControl : RuleSettingsControlBase<ScreenColorRuleSettings>
{
    private ComboBox _modeComboBox;

    public ScreenColorRuleSettingsControl()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new(10) };

        panel.Children.Add(new TextBlock
        {
            Text = "颜色判断模式",
            FontWeight = Avalonia.Media.FontWeight.Bold
        });

        _modeComboBox = new ComboBox
        {
            Width = 100,
            ItemsSource = new[] { "偏黑", "偏白" }
        };
        _modeComboBox.SelectionChanged += (s, e) =>
        {
            Settings.Mode = _modeComboBox.SelectedItem?.ToString() ?? "偏黑";
        };

        panel.Children.Add(_modeComboBox);
        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _modeComboBox.SelectedItem = Settings.Mode;
    }
}*/