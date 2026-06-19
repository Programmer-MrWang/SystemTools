using Avalonia.Controls;
using Avalonia.Data;
using ClassIsland.Core.Abstractions.Controls;
using SystemTools.Actions;

namespace SystemTools.Controls;

public class AdjustScreenBrightnessSettingsControl : ActionSettingsControlBase<AdjustScreenBrightnessSettings>
{
    private NumericUpDown _brightnessInput;

    public AdjustScreenBrightnessSettingsControl()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new(10) };

        panel.Children.Add(new TextBlock
        {
            Text = "设置屏幕亮度百分比",
            Margin = new(0, 0, 0, 5)
        });

        _brightnessInput = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 100,
            Increment = 1,
            FormatString = "0",
            Watermark = "输入 0-100 的整数"
        };
        panel.Children.Add(_brightnessInput);

        panel.Children.Add(new TextBlock
        {
            Text = "0 = 最暗, 100 = 最亮",
            Foreground = Avalonia.Media.Brushes.Gray,
            FontSize = 12,
            Margin = new(0, 5, 0, 0)
        });

        panel.Children.Add(new TextBlock
        {
            Text = "注意: 此功能使用 WMI 调用，需要显示器支持亮度调整。台式机外接显示器通常不支持此功能。在某些设备上可能需要管理员权限。",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Foreground = Avalonia.Media.Brushes.Gray,
            FontSize = 11,
            Margin = new(0, 10, 0, 0)
        });

        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        _brightnessInput[!NumericUpDown.ValueProperty] = new Binding(nameof(Settings.BrightnessPercent))
        {
            Source = Settings,
            Mode = BindingMode.TwoWay
        };
    }
}