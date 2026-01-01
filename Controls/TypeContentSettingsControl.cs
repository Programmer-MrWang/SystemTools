using Avalonia.Controls;
using Avalonia.Data;
using ClassIsland.Core.Abstractions.Controls;
using SystemTools.Settings;

namespace SystemTools.Controls;

public class TypeContentSettingsControl : ActionSettingsControlBase<TypeContentSettings>
{
    private TextBox _textBox;

    public TypeContentSettingsControl()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new(10) };

        panel.Children.Add(new TextBlock
        {
            Text = "要键入的内容:",
            FontWeight = Avalonia.Media.FontWeight.Bold
        });

        _textBox = new TextBox
        {
            Watermark = "输入要粘贴的文本内容",
            AcceptsReturn = true,
            Height = 100
        };

        panel.Children.Add(_textBox);
        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _textBox[!TextBox.TextProperty] = new Binding(nameof(Settings.Content))
        {
            Source = Settings
        };
    }
}