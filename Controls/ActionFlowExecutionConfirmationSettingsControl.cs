using Avalonia.Controls;
using Avalonia.Data;
using ClassIsland.Core.Abstractions.Controls;
using SystemTools.Settings;

namespace SystemTools.Controls;

public class ActionFlowExecutionConfirmationSettingsControl
    : ActionSettingsControlBase<ActionFlowExecutionConfirmationSettings>
{
    private readonly TextBox _promptNameTextBox;

    public ActionFlowExecutionConfirmationSettingsControl()
    {
        var panel = new StackPanel
        {
            Spacing = 10,
            Margin = new(10)
        };

        panel.Children.Add(new TextBlock
        {
            Text = "提示名称："
        });

        _promptNameTextBox = new TextBox
        {
            PlaceholderText = "请输入将在确认窗口中显示的自动化名称",
            AcceptsReturn = false
        };
        panel.Children.Add(_promptNameTextBox);

        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _promptNameTextBox.Bind(TextBox.TextProperty, new Binding(nameof(Settings.PromptName))
        {
            Source = Settings,
            Mode = BindingMode.TwoWay
        });
    }
}
