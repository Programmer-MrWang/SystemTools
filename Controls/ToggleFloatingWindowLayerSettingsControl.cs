using Avalonia.Controls;
using ClassIsland.Core.Abstractions.Controls;
using SystemTools.Settings;

namespace SystemTools.Controls;

/// <summary>
/// 切换悬浮窗层级行动的设置控件
/// </summary>
public class ToggleFloatingWindowLayerSettingsControl : ActionSettingsControlBase<ToggleFloatingWindowLayerSettings>
{
    private ComboBox _layerComboBox;
    private CheckBox _notifyCheckBox;

    public ToggleFloatingWindowLayerSettingsControl()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new(10) };

        panel.Children.Add(new TextBlock
        {
            Text = "目标层级:",
            FontWeight = Avalonia.Media.FontWeight.Bold
        });

        _layerComboBox = new ComboBox
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };
        _layerComboBox.Items.Add(new ComboBoxItem { Content = "切换（置底↔置顶）", Tag = -1 });
        _layerComboBox.Items.Add(new ComboBoxItem { Content = "置底", Tag = 0 });
        _layerComboBox.Items.Add(new ComboBoxItem { Content = "置顶", Tag = 1 });
        _layerComboBox.SelectedIndex = 0;

        panel.Children.Add(_layerComboBox);

        panel.Children.Add(new TextBlock
        {
            Text = "提示：选择\"切换\"会根据当前状态在置顶和置底之间切换，选择具体层级会直接设置到该层级。",
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

        _layerComboBox.SelectionChanged += OnLayerSelectionChanged;

        RestoreSettings();
    }

    private void RestoreSettings()
    {
        if (Settings == null) return;

        var index = Settings.TargetLayer switch
        {
            0 => 1,
            1 => 2,
            _ => 0
        };
        _layerComboBox.SelectedIndex = index;
    }

    private void OnLayerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_layerComboBox.SelectedItem is ComboBoxItem item && item.Tag is int layer)
        {
            Settings.TargetLayer = layer;
        }
    }
}
