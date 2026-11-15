using SystemTools.Contracts;

namespace SystemTools.Platforms.MacOS;

public sealed class SystemAuthenticationService : ISystemAuthentication
{
    public Capability Probe() => NativeBridge.Probe("authentication.probe");
    public async Task<AuthenticationResult> VerifyAsync(string reason, nint parentWindow, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var id = Guid.NewGuid().ToString("N");
        NativeBridge.Require(new { operation = "authentication.prepare", id });
        try
        {
            using var cancellation = token.Register(() => NativeBridge.Call(new { operation = "authentication.cancel", id }));
            var result = await Task.Run(() => NativeBridge.Require(new { operation = "authentication.verify", id, reason })).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return new(result.GetProperty("verified").GetBoolean(), "系统认证已结束。");
        }
        finally { NativeBridge.Call(new { operation = "authentication.release", id }); }
    }
}
