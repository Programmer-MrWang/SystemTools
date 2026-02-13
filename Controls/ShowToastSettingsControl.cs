using Avalonia.Controls;
using Avalonia.Data;
using ClassIsland.Core.Abstractions.Controls;
using SystemTools.Settings;

namespace SystemTools.Controls;

public class ShowToastSettingsControl : ActionSettingsControlBase<ShowToastSettings>
{
    private TextBox _titleBox;
    private TextBox _contentBox;

    public ShowToastSettingsControl()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new(10) };

        // 通知标题
        panel.Children.Add(new TextBlock
        {
            Text = "通知标题:",
            Margin = new(0, 5, 0, 0)
        });

        _titleBox = new TextBox
        {
            Watermark = "输入通知标题",
            Text = "SystemTools"
        };
        panel.Children.Add(_titleBox);

        // 通知内容
        panel.Children.Add(new TextBlock
        {
            Text = "通知内容:",
            Margin = new(0, 10, 0, 0)
        });

        _contentBox = new TextBox
        {
            Watermark = "输入通知内容",
            AcceptsReturn = true,
            Height = 80
        };
        panel.Children.Add(_contentBox);

        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        _titleBox[!TextBox.TextProperty] = new Binding(nameof(Settings.Title))
        {
            Source = Settings,
            Mode = BindingMode.TwoWay
        };

        _contentBox[!TextBox.TextProperty] = new Binding(nameof(Settings.Content))
        {
            Source = Settings,
            Mode = BindingMode.TwoWay
        };
    }
}