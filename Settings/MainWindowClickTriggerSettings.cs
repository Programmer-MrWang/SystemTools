using Avalonia.Controls;
using Avalonia.Media;
using ClassIsland.Core.Abstractions.Controls;

namespace SystemTools.Triggers;

public sealed class MainWindowClickTriggerSettings : TriggerSettingsControlBase<MainWindowClickTriggerConfig>
{
    public MainWindowClickTriggerSettings()
    {
        Content = new TextBlock
        {
            Margin = new(10),
            Text = "在ClassIsland主界面上进行左键单击操作时触发。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray
        };
    }
}
