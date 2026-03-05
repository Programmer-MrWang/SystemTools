using Avalonia.Controls;
using ClassIsland.Core.Abstractions.Controls;
using SystemTools.Triggers;

namespace SystemTools.Settings;

public class FloatingWindowTriggerSettings : TriggerSettingsControlBase<FloatingWindowTriggerConfig>
{
    private readonly TextBox _iconTextBox;

    public FloatingWindowTriggerSettings()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new(10) };

        panel.Children.Add(new TextBlock
        {
            Text = "悬浮窗按钮图标（示例：/uE7C3）",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        _iconTextBox = new TextBox
        {
            Watermark = "/uE7C3"
        };
        _iconTextBox.TextChanged += (_, _) => { Settings.Icon = _iconTextBox.Text ?? string.Empty; };

        panel.Children.Add(_iconTextBox);
        panel.Children.Add(new TextBlock
        {
            Text = "每个“从悬浮窗触发”触发器会在浮窗里生成一个按钮。",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Foreground = Avalonia.Media.Brushes.Gray
        });

        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _iconTextBox.Text = Settings.Icon;
    }
}
