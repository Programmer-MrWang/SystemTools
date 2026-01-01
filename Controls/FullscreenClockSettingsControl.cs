using Avalonia.Controls;
using Avalonia.Media;
using ClassIsland.Core.Abstractions.Controls;
using SystemTools.Settings;

namespace SystemTools.Controls;

public class FullscreenClockSettingsControl : ActionSettingsControlBase<FullscreenClockSettings>
{
    public FullscreenClockSettingsControl()
    {
        var panel = new StackPanel { Spacing = 8, Margin = new(10) };

        panel.Children.Add(new TextBlock
        {
            Text = "沉浸式时钟",
            FontWeight = FontWeight.Bold,
            FontSize = 14
        });

        panel.Children.Add(new TextBlock
        {
            Text = "本服务由 QQHKX 提供。您需要联网。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray
        });

        var linkText = new TextBlock
        {
            Text = "该项目仓库：https://github.com/QQHKX/immersive-clock",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Blue,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };

        linkText.PointerPressed += (s, e) =>
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/QQHKX/immersive-clock",
                UseShellExecute = true
            });
        };

        panel.Children.Add(linkText);

        Content = panel;
    }
}