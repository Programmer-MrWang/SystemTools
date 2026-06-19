using System;
using System.Linq;
using WinMedia = Windows.Media.Control;

namespace SystemTools;

public partial class Plugin
{
    private static DateTime _lastMediaRuleCheckAt = DateTime.MinValue;
    private static bool _lastMediaRuleResult;

    private static bool HandleMediaMusicPlayingRule(object? settings)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17134))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        if (now - _lastMediaRuleCheckAt < TimeSpan.FromMilliseconds(800))
        {
            return _lastMediaRuleResult;
        }

        try
        {
            var manager = WinMedia.GlobalSystemMediaTransportControlsSessionManager.RequestAsync()
                .AsTask().GetAwaiter().GetResult();

            var sessions = manager?.GetSessions();
            var isPlaying = sessions != null && sessions.Any(session =>
            {
                var playbackInfo = session.GetPlaybackInfo();
                return playbackInfo?.PlaybackStatus == WinMedia.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            });

            _lastMediaRuleResult = isPlaying;
            _lastMediaRuleCheckAt = now;
            return isPlaying;
        }
        catch
        {
            _lastMediaRuleCheckAt = now;
            return _lastMediaRuleResult;
        }
    }
}
