using SystemTools.Contracts;
namespace SystemTools.Platforms.MacOS;

public sealed class DesktopSessionService : IDesktopSession, IDisposable
{
    private readonly CompanionClient _client;
    private readonly IDisposable _ownership;
    internal DesktopSessionService(CompanionClient client) { _client = client; _ownership = client.Retain(); }
    public Capability Probe(string operation) => operation switch
    {
        "shutdown" or "restart" or "sleep" or "lock" or "theme.read" or "theme.set" =>
            new(CapabilityState.Available, "通过自有助手执行；首次使用可能需要允许助手自动化 System Events。"),
        "desktop" => new(CapabilityState.Unsupported, "macOS 没有可在此验证的通用公开显示桌面接口；不依赖用户可改的快捷键。"),
        "accent.read" or "accent.set" => new(CapabilityState.Unsupported, "macOS 不提供此插件可用的任意系统强调色写入接口。"),
        _ => new(CapabilityState.Unsupported, "未知系统操作。")
    };
    public async Task<string> ExecuteAsync(string operation, string? value, CancellationToken token)
    {
        var available = Probe(operation); if (!available.CanExecute) throw new NativeFeatureException(available.State, available.Reason);
        var result = await _client.RequireAsync("system.execute", new { action = operation, value }, token, TimeSpan.FromSeconds(65));
        return result.TryGetProperty("value", out var response) ? response.GetString() ?? "" : "";
    }
    public void Dispose() => _ownership.Dispose();
}
