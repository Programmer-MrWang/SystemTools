using SystemTools.Contracts;

namespace SystemTools.Common.Media;

public sealed class UnsupportedAuthentication : ISystemAuthentication
{
    public Capability Probe() => new(CapabilityState.Unsupported, "当前平台尚无可验证的系统认证后端，原认证配置已保留。");
    public Task<AuthenticationResult> VerifyAsync(string reason, nint parentWindow, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(new AuthenticationResult(false, Probe().Reason));
    }
}

public sealed class UnsupportedMediaSessions : IMediaSessions
{
    public Capability Probe() => new(CapabilityState.Unsupported, "当前平台没有已验证的通用媒体状态来源。");
    public Task<IReadOnlyList<MediaSession>> ReadAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        throw new NativeFeatureException(CapabilityState.Unsupported, Probe().Reason);
    }
}
