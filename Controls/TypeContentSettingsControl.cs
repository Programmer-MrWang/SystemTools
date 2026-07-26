using Avalonia.Controls;
using Avalonia.Data;
using ClassIsland.Core.Abstractions.Controls;
using SystemTools.Settings;

namespace SystemTools.Controls;

public class TypeContentSettingsControl : ActionSettingsControlBase<TypeContentSettings>
{
    private CheckBox _notifyCheckBox;
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
        _notifyCheckBox = new CheckBox { Content = "当执行时发出提醒" };
        _notifyCheckBox.IsCheckedChanged += (s, e) => { Settings.NotifyOnExecute = _notifyCheckBox.IsChecked ?? false; };
        panel.Children.Add(_notifyCheckBox);

        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _notifyCheckBox.IsChecked = Settings.NotifyOnExecute;
        _textBox.Bind(TextBox.TextProperty, new Binding(nameof(Settings.Content))
        {
            Source = Settings
        });
    }
}
