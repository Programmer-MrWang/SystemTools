using Avalonia.Controls;
using ClassIsland.Core.Abstractions.Controls;

namespace SystemTools.Triggers;

public class ActionInProgressTriggerSettings : TriggerSettingsControlBase<ActionInProgressTriggerConfig>
{
    private Avalonia.Controls.TextBox _textBox;

    public ActionInProgressTriggerSettings()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new(10) };

        panel.Children.Add(new TextBlock
        {
            Text = "行动进行时触发器",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            FontSize = 14
        });

        var inputPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 10
        };

        inputPanel.Children.Add(new TextBlock
        {
            Text = "输入行动”触发指定触发器“中指定的字符：",
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });

        _textBox = new Avalonia.Controls.TextBox
        {
            Watermark = "输入行动”触发指定触发器“中指定的字符",
            Width = 200,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        _textBox.TextChanged += (s, e) => Settings.TriggerId = _textBox.Text ?? "";

        inputPanel.Children.Add(_textBox);
        panel.Children.Add(inputPanel);

        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _textBox.Text = Settings.TriggerId;
    }
}