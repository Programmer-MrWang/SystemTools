using Avalonia.Controls;
using Avalonia.Data;
using ClassIsland.Core.Abstractions.Controls;
using SystemTools.Settings;

namespace SystemTools.Controls;

public class DisableDeviceSettingsControl : ActionSettingsControlBase<DisableDeviceSettings>
{
    private TextBox _deviceIdBox;
    private CheckBox _notifyCheckBox;

    public DisableDeviceSettingsControl()
    {
        var panel = new Avalonia.Controls.StackPanel { Spacing = 10, Margin = new(10) };

        panel.Children.Add(new Avalonia.Controls.TextBlock
        {
            Text = "设备ID:",
            Margin = new(0, 10, 0, 5)
        });

        _deviceIdBox = new TextBox
        {
            Watermark = "输入设备实例路径"
        };
        panel.Children.Add(_deviceIdBox);

        panel.Children.Add(new Avalonia.Controls.TextBlock
        {
            Text = "查询设备ID方法：\n1. 打开设备管理器\n2. 找到目标设备\n3. 右键 → 属性 → 详细信息\n4. 选择\"设备实例路径\"\n5. 复制属性值",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Foreground = Avalonia.Media.Brushes.Gray,
            FontSize = 12,
            Margin = new(0, 10, 0, 0)
        });        _notifyCheckBox = new CheckBox { Content = "当执行时发出提醒" };
        _notifyCheckBox.IsCheckedChanged += (s, e) => { Settings.NotifyOnExecute = _notifyCheckBox.IsChecked ?? false; };
        panel.Children.Add(_notifyCheckBox);

        

        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _notifyCheckBox.IsChecked = Settings.NotifyOnExecute;
        _deviceIdBox.Bind(TextBox.TextProperty, new Binding(nameof(Settings.DeviceId))
        {
            Source = Settings
        });
    }
}