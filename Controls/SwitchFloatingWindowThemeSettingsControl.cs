using Avalonia.Controls;
using ClassIsland.Core.Abstractions.Controls;
using SystemTools.Settings;

namespace SystemTools.Controls;

/// <summary>
/// 切换悬浮窗主题行动的设置控件
/// </summary>
public class SwitchFloatingWindowThemeSettingsControl : ActionSettingsControlBase<SwitchFloatingWindowThemeSettings>
{
    private ComboBox _themeComboBox;
    private CheckBox _notifyCheckBox;

    public SwitchFloatingWindowThemeSettingsControl()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new(10) };

        panel.Children.Add(new TextBlock
        {
            Text = "目标主题:",
            FontWeight = Avalonia.Media.FontWeight.Bold
        });

        _themeComboBox = new ComboBox
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };
        _themeComboBox.Items.Add(new ComboBoxItem { Content = "切换到下一个", Tag = -1 });
        _themeComboBox.Items.Add(new ComboBoxItem { Content = "跟随系统", Tag = 0 });
        _themeComboBox.Items.Add(new ComboBoxItem { Content = "浅色", Tag = 1 });
        _themeComboBox.Items.Add(new ComboBoxItem { Content = "深色", Tag = 2 });
        _themeComboBox.SelectedIndex = 0;

        panel.Children.Add(_themeComboBox);

        panel.Children.Add(new TextBlock
        {
            Text = "提示：选择\"切换到下一个\"会按 跟随系统→浅色→深色→跟随系统 循环切换，选择具体主题会直接设置到该主题。",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Opacity = 0.7,
            FontSize = 12
        });        _notifyCheckBox = new CheckBox { Content = "当执行时发出提醒" };
        _notifyCheckBox.IsCheckedChanged += (s, e) => { Settings.NotifyOnExecute = _notifyCheckBox.IsChecked ?? false; };
        panel.Children.Add(_notifyCheckBox);

        

        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _notifyCheckBox.IsChecked = Settings.NotifyOnExecute;

        _themeComboBox.SelectionChanged += OnThemeSelectionChanged;

        RestoreSettings();
    }

    private void RestoreSettings()
    {
        if (Settings == null) return;

        var index = Settings.TargetTheme switch
        {
            0 => 1,
            1 => 2,
            2 => 3,
            _ => 0
        };
        _themeComboBox.SelectedIndex = index;
    }

    private void OnThemeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_themeComboBox.SelectedItem is ComboBoxItem item && item.Tag is int theme)
        {
            Settings.TargetTheme = theme;
        }
    }
}
