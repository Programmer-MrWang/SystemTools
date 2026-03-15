using Avalonia.Controls;
using ClassIsland.Core.Abstractions.Controls;
using SystemTools.Triggers;

namespace SystemTools.Controls;

public class UsbDeviceTriggerSettings : TriggerSettingsControlBase<UsbDeviceTriggerConfig>
{
    private CheckBox _usbOnlyCheckBox;

    public UsbDeviceTriggerSettings()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new(10) };

        _usbOnlyCheckBox = new CheckBox
        {
            Content = "仅U盘设备",
            IsChecked = true,
            Margin = new(0, 5, 0, 0)
        };
        _usbOnlyCheckBox.IsCheckedChanged += (s, e) =>
        {
            Settings.OnlyUsbStorage = _usbOnlyCheckBox.IsChecked ?? true;
        };
        panel.Children.Add(_usbOnlyCheckBox);

        panel.Children.Add(new TextBlock
        {
            Text = "勾选后使仅当U盘设备插入时触发。",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Foreground = Avalonia.Media.Brushes.Gray
        });

        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _usbOnlyCheckBox.IsChecked = Settings.OnlyUsbStorage;
    }
}