namespace SystemTools.Common.Automation;

/// <summary>Preserves grouped restoration across transient action provider instances.</summary>
public sealed class SettingRestoreTransactions
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ActionRestoreStore<string> _original = new();
    private readonly Dictionary<(Guid, object), (string Operation, string Applied)> _applied = new();
    public async Task ApplyAsync(SystemTools.Contracts.IDesktopSession session, Guid group, object owner, string operation,
        string value, bool revertable, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            var original = await session.ExecuteAsync(operation + ".read", null, token);
            try
            {
                await session.ExecuteAsync(operation + ".set", value, token);
                if (await session.ExecuteAsync(operation + ".read", null, token) != value) throw new IOException("系统未确认请求的外观值。");
                token.ThrowIfCancellationRequested();
            }
            catch
            {
                if (await session.ExecuteAsync(operation + ".read", null, CancellationToken.None) == value)
                    await session.ExecuteAsync(operation + ".set", original, CancellationToken.None);
                throw;
            }
            if (revertable)
            {
                _original.Capture(group, owner, operation, original);
                _applied[(group, owner)] = (operation, value);
            }
        }
        finally { _gate.Release(); }
    }
    public async Task RevertAsync(SystemTools.Contracts.IDesktopSession session, Guid group, object owner)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_applied.Remove((group, owner), out var expected) || !_original.TryRestore(group, owner, out var original)) return;
            if (await session.ExecuteAsync(expected.Operation + ".read", null, CancellationToken.None) == expected.Applied)
                await session.ExecuteAsync(expected.Operation + ".set", original, CancellationToken.None);
        }
        finally { _gate.Release(); }
    }
}
