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
            Text = "注意：此热键为全局热键，在任何窗口按下F9都会触发。",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Foreground = Avalonia.Media.Brushes.Orange
        });

        Content = panel;
    }
}