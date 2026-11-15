using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SystemTools.Contracts;
using SystemTools.Services;
using Windows.Security.Credentials.UI;
using WinMedia = Windows.Media.Control;

namespace SystemTools.Platforms.Windows;

public sealed class WindowsSystemAuthentication : ISystemAuthentication, IAsyncCapabilityProbe
{
    private Capability _last = new(CapabilityState.PermissionRequired, "请先检查 Windows Hello 配置。");
    public Capability Probe() => _last;
    public async Task<Capability> ProbeAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = await WindowsHelloService.CheckSupportAsync(requireFaceEnrollment: true).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        _last = result.IsAvailable ? new(CapabilityState.Available, result.Message) :
            new(CapabilityState.MissingDevice, result.Message);
        return _last;
    }
    public async Task<AuthenticationResult> VerifyAsync(string reason, nint parentWindow, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var capability = await ProbeAsync(token).ConfigureAwait(false);
        if (!capability.CanExecute) return new(false, capability.Reason);
        if (parentWindow == 0) return new(false, "请从 ClassIsland 认证窗口发起验证。");
        var result = await WindowsHelloService.RequestVerificationAsync(parentWindow, reason).WaitAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return new(result == UserConsentVerificationResult.Verified,
            result == UserConsentVerificationResult.Verified ? "系统验证已通过。" : "系统验证未通过或已取消。");
    }
}

public sealed class WindowsMediaSessions : IMediaSessions
{
    public Capability Probe() => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)
        ? new(CapabilityState.Available) : new(CapabilityState.Unsupported, "当前 Windows 版本不支持系统媒体会话。");
    public async Task<IReadOnlyList<MediaSession>> ReadAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Probe().CanExecute) return [];
        var manager = await WinMedia.GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(token).ConfigureAwait(false);
        var result = new List<MediaSession>();
        foreach (var session in manager.GetSessions())
        {
            token.ThrowIfCancellationRequested();
            var properties = await session.TryGetMediaPropertiesAsync().AsTask(token).ConfigureAwait(false);
            var playing = session.GetPlaybackInfo()?.PlaybackStatus == WinMedia.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            result.Add(new(session.SourceAppUserModelId, playing ? "Playing" : "Paused", properties.Title, properties.Artist));
        }
        return result;
    }
}
