using Avalonia.Controls;
using Avalonia.Layout;
using ClassIsland.Core.Abstractions.Controls;
using SystemTools.Settings;

namespace SystemTools.Controls;

public class AccentColorSettingsControl : ActionSettingsControlBase<AccentColorSettings>
{
    private readonly ColorPicker _colorPicker;

    public AccentColorSettingsControl()
    {
        var panel = new StackPanel { Spacing = 10, Margin = new(10) };
        panel.Children.Add(new TextBlock { Text = "切换系统强调色", FontSize = 14, FontWeight = Avalonia.Media.FontWeight.Bold });

        _colorPicker = new ColorPicker
        {
            IsAlphaEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _colorPicker.ColorChanged += (_, _) => Settings.ColorHex = _colorPicker.Color.ToString();

        panel.Children.Add(_colorPicker);
        Content = panel;
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        if (Avalonia.Media.Color.TryParse(Settings.ColorHex, out var color))
            _colorPicker.Color = color;
    }
}
