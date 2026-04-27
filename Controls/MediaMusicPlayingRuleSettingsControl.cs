using Avalonia.Controls;
using Avalonia.Media;
using ClassIsland.Core.Abstractions.Controls;
using SystemTools.Rules;

namespace SystemTools.Controls;

public class MediaMusicPlayingRuleSettingsControl : RuleSettingsControlBase<MediaMusicPlayingRuleSettings>
{
    public MediaMusicPlayingRuleSettingsControl()
    {
        Content = new TextBlock
        {
            Text = "此规则会通过系统媒体传输控制（SMTC）判断是否有媒体正在播放。",
            Margin = new Thickness(10),
            TextWrapping = TextWrapping.Wrap
        };
    }
}
