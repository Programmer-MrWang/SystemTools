using SystemTools.Contracts;

namespace SystemTools.Common.Layout;

/// <summary>Exactly one of confirmation, timeout and disposal wins; failed rollback remains observable.</summary>
public sealed class TimedDisplayChange : IDisplayChange
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Func<Task> _validate, _rollback;
    private readonly Action _release;
    private bool _resolved;
    public Task Completion => _completion.Task;
    public TimedDisplayChange(Func<Task> validate, Func<Task> rollback, Action release, TimeSpan? timeout = null)
    {
        _validate = validate; _rollback = rollback; _release = release;
        _ = ExpireAsync(timeout ?? TimeSpan.FromSeconds(15));
    }
    private async Task ExpireAsync(TimeSpan delay)
    {
        try { await Task.Delay(delay, _stop.Token); await DisposeAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Completion carries rollback failure to the owner. */ }
    }
    public async Task ConfirmAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (_resolved) throw new InvalidOperationException("显示设置确认已过期或事务已结束。");
            token.ThrowIfCancellationRequested();
            await _validate();
            token.ThrowIfCancellationRequested();
            Resolve(null);
        }
        finally { _gate.Release(); }
    }
    private void Resolve(Exception? error)
    {
        _resolved = true; _stop.Cancel();
        try { _release(); }
        finally { if (error is null) _completion.TrySetResult(); else _completion.TrySetException(error); }
    }
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!_resolved)
            {
                Exception? failure = null;
                try { await _rollback(); } catch (Exception error) { failure = error; }
                Resolve(failure);
            }
        }
        finally { _gate.Release(); }
        await Completion;
    }
}
