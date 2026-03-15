using Avalonia.Controls;
using ClassIsland.Core.Abstractions.Controls;

namespace SystemTools.Triggers;

public class LongIdleTriggerSettings : TriggerSettingsControlBase<LongIdleTriggerConfig>
{
    private readonly NumericUpDown _minutesInput;

    public LongIdleTriggerSettings()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new(10) };

        var inputPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8
        };

        inputPanel.Children.Add(new TextBlock
        {
            Text = "未操作时长（分钟）:",
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });

        _minutesInput = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 1440,
            Increment = 1,
            Width = 120
        };
        _minutesInput.ValueChanged += (_, _) => Settings.IdleMinutes = (int)(_minutesInput.Value ?? 1);

        inputPanel.Children.Add(_minutesInput);
        panel.Children.Add(inputPanel);

        panel.Children.Add(new TextBlock
        {
            Text = "当超过设定时间未进行鼠标/键盘操作时触发；\n若启用恢复，则当再次操作电脑时会自动恢复。",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Foreground = Avalonia.Media.Brushes.Gray
        });

        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _minutesInput.Value = Settings.IdleMinutes;
    }
}
