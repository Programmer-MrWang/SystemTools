namespace SystemTools.Common.Automation;

public sealed class ScheduledOperation : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _pending;
    public DateTimeOffset? Due { get; private set; }
    public Exception? LastError { get; private set; }
    public Task Completion { get; private set; } = Task.CompletedTask;
    public void Schedule(TimeSpan delay, Func<CancellationToken, Task> execute, CancellationToken stopping)
    {
        if (delay < TimeSpan.Zero || delay > TimeSpan.FromDays(7)) throw new ArgumentOutOfRangeException(nameof(delay), "计时必须在 0 到 7 天内。");
        lock (_gate)
        {
            Cancel(); LastError = null;
            var stop = CancellationTokenSource.CreateLinkedTokenSource(stopping); _pending = stop;
            Due = DateTimeOffset.UtcNow + delay;
            Completion = Run(delay, execute, stop);
        }
    }
    private async Task Run(TimeSpan delay, Func<CancellationToken, Task> execute, CancellationTokenSource stop)
    {
        try
        {
            await Task.Delay(delay, stop.Token).ConfigureAwait(false);
            stop.Token.ThrowIfCancellationRequested();
            await execute(stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception error) { lock (_gate) LastError = error; }
        finally { lock (_gate) { if (ReferenceEquals(_pending, stop)) { _pending = null; Due = null; } stop.Dispose(); } }
    }
    public void Cancel() { lock (_gate) { _pending?.Cancel(); Due = null; } }
    public void Dispose() => Cancel();
}
