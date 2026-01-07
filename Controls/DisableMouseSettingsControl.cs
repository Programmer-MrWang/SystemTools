using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ClassIsland.Core.Abstractions.Controls;

namespace SystemTools.Controls;

public class DisableMouseSettingsControl : ActionSettingsControlBase
{
    public DisableMouseSettingsControl()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new(10) };

        var warningBorder = new Border
        {
            Background = new SolidColorBrush(Colors.OrangeRed) { Opacity = 0.1 },
            BorderBrush = new SolidColorBrush(Colors.OrangeRed),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10)
        };

        var warningText = new TextBlock
        {
            Text = "警告：使用此功能请确保您有其他输入设备，且在接下来的行动中设置了“启用鼠标”。如未成功恢复，请使用键盘等方式运行插件目录下的“huifu.bat”来恢复启用鼠标。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Colors.OrangeRed),
            FontWeight = FontWeight.Bold
        };

        warningBorder.Child = warningText;
        panel.Children.Add(warningBorder);

        Content = panel;
    }
}