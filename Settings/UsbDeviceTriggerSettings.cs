using Avalonia.Controls;
using ClassIsland.Core.Abstractions.Controls;

namespace SystemTools.Triggers;

public class UsbDeviceTriggerSettings : TriggerSettingsControlBase<UsbDeviceTriggerConfig>
{
    public UsbDeviceTriggerSettings()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new(10) };

        panel.Children.Add(new TextBlock
        {
            Text = "USB设备插入触发器",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            FontSize = 14
        });

        panel.Children.Add(new TextBlock
        {
            Text = "当USB设备插入时触发。",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Foreground = Avalonia.Media.Brushes.Gray
        });

        Content = panel;
    }
}