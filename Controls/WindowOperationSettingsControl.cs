using Avalonia.Controls;
using ClassIsland.Core.Abstractions.Controls;
using SystemTools.Settings;

namespace SystemTools.Controls;

public class WindowOperationSettingsControl : ActionSettingsControlBase<WindowOperationSettings>
{
    private CheckBox _notifyCheckBox;
    private ComboBox _operationComboBox;

    public WindowOperationSettingsControl()
    {
        var panel = new Avalonia.Controls.StackPanel { Spacing = 10, Margin = new(10) };

        panel.Children.Add(new Avalonia.Controls.TextBlock
        {
            Text = "请选择对于活动窗口的操作：",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            FontSize = 14
        });

        _operationComboBox = new ComboBox
        {
            Width = 150,
            ItemsSource = new[] { "最大化", "最小化", "向下还原", "关闭窗口" }
        };

        _operationComboBox.SelectionChanged += (s, e) =>
        {
            Settings.Operation = _operationComboBox.SelectedItem?.ToString() ?? "最大化";
        };

        panel.Children.Add(_operationComboBox);
        _notifyCheckBox = new CheckBox { Content = "当执行时发出提醒" };
        _notifyCheckBox.IsCheckedChanged += (s, e) => { Settings.NotifyOnExecute = _notifyCheckBox.IsChecked ?? false; };
        panel.Children.Add(_notifyCheckBox);

        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _notifyCheckBox.IsChecked = Settings.NotifyOnExecute;
        _operationComboBox.SelectedItem = Settings.Operation;
    }
}
