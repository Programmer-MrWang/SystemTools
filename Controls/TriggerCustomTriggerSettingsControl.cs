using Avalonia.Controls;
using Avalonia.Data;
using ClassIsland.Core.Abstractions.Controls;
using SystemTools.Settings;

namespace SystemTools.Controls;

public class TriggerCustomTriggerSettingsControl : ActionSettingsControlBase<TriggerCustomTriggerSettings>
{
    private Avalonia.Controls.TextBox _triggerIdTextBox;

    public TriggerCustomTriggerSettingsControl()
    {
        var panel = new Avalonia.Controls.StackPanel { Spacing = 10, Margin = new(10) };

        var labelPanel = new Avalonia.Controls.StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 5,
            Margin = new(0, 10, 0, 0)
        };
        labelPanel.Children.Add(new Avalonia.Controls.TextBlock
        {
            Text = "请指定与”行动进行时“触发器相同的字符：",
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });
        panel.Children.Add(labelPanel);

        _triggerIdTextBox = new Avalonia.Controls.TextBox
        {
            Watermark = "输入与”行动进行时“触发器相同的字符",
            Height = 35
        };
        panel.Children.Add(_triggerIdTextBox);

        panel.Children.Add(new TextBlock
        {
            Text = "警告：该行动在ClassIsland全局中只能使用一次",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Foreground = Avalonia.Media.Brushes.Orange,
            Margin = new(0, 10, 0, 0)
        });

        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _triggerIdTextBox[!Avalonia.Controls.TextBox.TextProperty] = new Binding(nameof(Settings.TriggerId))
        {
            Source = Settings
        };
    }
}