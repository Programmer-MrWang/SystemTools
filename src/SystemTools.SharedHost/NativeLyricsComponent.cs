using Avalonia.Controls;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using SystemTools.Common.Media;
using SystemTools.Models.ComponentSettings;
using SystemTools.Rules;

namespace SystemTools;

[ComponentInfo("A1B2C3D4-E5F6-7890-ABCD-EF1234567891", "音乐软件歌词显示", "\uEBDC", "根据已选择的歌词文件与真实媒体进度显示歌词")]
public sealed class NativeLyricsComponent : ComponentBase<LyricsDisplaySettings>
{
    private readonly TextBlock _text = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private string? _path;
    private DateTime _written;
    private LrcTimeline? _timeline;
    public NativeLyricsComponent()
    {
        Content = _text;
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { Refresh(); _timer.Start(); };
        Unloaded += (_, _) => { _timer.Stop(); _timeline = null; _path = null; _text.Text = ""; };
    }
    private void Refresh()
    {
        try
        {
            var preferences = IAppHost.GetService<MediaPreferencesStore>().Current;
            var playing = IAppHost.GetService<MediaPlaybackMonitor>().Current.FirstOrDefault(session => session.State == "Playing" && session.Position is not null);
            if (playing?.Position is not { } position || !File.Exists(preferences.LyricsFile)) { _text.Text = ""; return; }
            var written = File.GetLastWriteTimeUtc(preferences.LyricsFile);
            if (_timeline is null || _path != preferences.LyricsFile || _written != written)
            {
                if (new FileInfo(preferences.LyricsFile).Length > 1024 * 1024) throw new InvalidDataException();
                _timeline = new(File.ReadAllText(preferences.LyricsFile)); _written = written; _path = preferences.LyricsFile;
            }
            _text.Text = _timeline.At(position);
        }
        catch (IOException) { _text.Text = ""; }
        catch (UnauthorizedAccessException) { _text.Text = ""; }
    }
}

public sealed class NativeLyricsSettingsControl : ComponentBase<LyricsDisplaySettings>
{
    public NativeLyricsSettingsControl() => Content = new TextBlock { Text = "在“相机、语音与媒体”中选择与当前曲目对应的 LRC 文件。没有真实播放进度时不显示歌词。", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
}

public sealed class NativeMediaRuleSettingsControl : RuleSettingsControlBase<MediaMusicPlayingRuleSettings>
{
    public NativeMediaRuleSettingsControl() => Content = new TextBlock { Text = "根据当前桌面的真实媒体会话判断播放状态。", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
}
