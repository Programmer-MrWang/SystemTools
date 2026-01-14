using Avalonia.Controls;
using ClassIsland.Core.Abstractions.Controls;

namespace SystemTools.Triggers;

public class HotkeyTriggerSettings : TriggerSettingsControlBase<HotkeyTriggerConfig>
{
    public HotkeyTriggerSettings()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new(10) };

        panel.Children.Add(new TextBlock
        {
            Text = "按下F9键触发器",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            FontSize = 14
        });

        panel.Children.Add(new TextBlock
        {
            Text = "当按下F9键时触发自动化。",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Foreground = Avalonia.Media.Brushes.Gray
        });

        panel.Children.Add(new TextBlock
        {
            Text = "警告：该触发器在ClassIsland全局中只能使用一次",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Foreground = Avalonia.Media.Brushes.Orange,
            Margin = new(0, 10, 0, 0)
        });

        Content = panel;
    }
}