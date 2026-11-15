using System.Globalization;
using SystemTools.Common.Media;
using SystemTools.Common.Processes;
using SystemTools.Contracts;

namespace SystemTools.Platforms.Linux;

public sealed class MediaSessionsService : IMediaSessions
{
    public Capability Probe() => MediaFiles.FindExecutable("playerctl") is not null &&
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS"))
        ? new(CapabilityState.Available) : new(CapabilityState.MissingDependency, "需要 playerctl 及当前用户的桌面媒体会话。");
    public async Task<IReadOnlyList<MediaSession>> ReadAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var status = Probe();
        if (!status.CanExecute) throw new NativeFeatureException(status.State, status.Reason);
        var tool = MediaFiles.FindExecutable("playerctl")!;
        var players = await CommandRunner.RunAsync(tool, ["-l"], token, TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        if (players.ExitCode == 1 && players.StandardError.Contains("No players found", StringComparison.OrdinalIgnoreCase)) return [];
        if (players.ExitCode != 0) throw new IOException("无法读取桌面媒体会话，请检查会话服务。");
        var sessions = new List<MediaSession>();
        foreach (var player in players.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct())
        {
            var state = await CommandRunner.RunAsync(tool, ["--player", player, "status"], token, TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            if (state.ExitCode != 0) continue;
            var metadata = await CommandRunner.RunAsync(tool, ["--player", player, "metadata", "--format", "{{title}}\u001f{{artist}}"], token, TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            var fields = metadata.ExitCode == 0 ? metadata.StandardOutput.TrimEnd('\r', '\n').Split('\u001f') : [];
            var position = await CommandRunner.RunAsync(tool, ["--player", player, "position"], token, TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            var seconds = double.TryParse(position.StandardOutput.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? Math.Max(0, number) : (double?)null;
            sessions.Add(new(player, state.StandardOutput.Trim(), fields.ElementAtOrDefault(0) ?? "", fields.ElementAtOrDefault(1) ?? "", Position: seconds is { } value ? TimeSpan.FromSeconds(value) : null));
        }
        return sessions;
    }
}
