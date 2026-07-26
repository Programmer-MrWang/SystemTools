using Avalonia.Controls;
using ClassIsland.Core.Abstractions.Controls;
using SystemTools.Settings;

namespace SystemTools.Controls;

public class ThemeSettingsControl : ActionSettingsControlBase<ThemeSettings>
{
    private ComboBox _themeComboBox;
    private CheckBox _notifyCheckBox;

    public ThemeSettingsControl()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new(10) };

        panel.Children.Add(new TextBlock
        {
            Text = "切换主题色",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            FontSize = 14
        });

        var comboPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 10
        };

        comboPanel.Children.Add(new TextBlock
        {
            Text = "选择主题:",
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });

        _themeComboBox = new ComboBox
        {
            Width = 100,
            ItemsSource = new[] { "浅色", "深色" }
        };

        _themeComboBox.SelectionChanged += (s, e) =>
        {
            Settings.Theme = _themeComboBox.SelectedItem?.ToString() ?? "浅色";
        };

        comboPanel.Children.Add(_themeComboBox);
        panel.Children.Add(comboPanel);        _notifyCheckBox = new CheckBox { Content = "当执行时发出提醒" };
        _notifyCheckBox.IsCheckedChanged += (s, e) => { Settings.NotifyOnExecute = _notifyCheckBox.IsChecked ?? false; };
        panel.Children.Add(_notifyCheckBox);

        

        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _notifyCheckBox.IsChecked = Settings.NotifyOnExecute;
        _themeComboBox.SelectedItem = Settings.Theme;
    }
}