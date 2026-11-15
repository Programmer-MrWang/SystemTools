namespace SystemTools.Common.Media;

public sealed class ConversationTurns(CancellationToken stopping) : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _stop = CancellationTokenSource.CreateLinkedTokenSource(stopping);
    private readonly HashSet<Task> _tasks = [];
    private CancellationTokenSource? _current;
    private long _epoch;

    public Task SubmitAsync(string text, Func<string, CancellationToken, Task<string>> respond,
        Func<string, CancellationToken, Task> speak, Action<string> publish)
    {
        lock (_sync)
        {
            _stop.Token.ThrowIfCancellationRequested();
            _current?.Cancel();
            var source = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            _current = source;
            var epoch = ++_epoch;
            var task = Task.Run(() => ExecuteAsync(text, respond, speak, publish, source, epoch));
            _tasks.Add(task);
            _ = task.ContinueWith(completed => { lock (_sync) _tasks.Remove(completed); }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return task;
        }
    }

    private async Task ExecuteAsync(string text, Func<string, CancellationToken, Task<string>> respond,
        Func<string, CancellationToken, Task> speak, Action<string> publish, CancellationTokenSource source, long epoch)
    {
        try
        {
            await _gate.WaitAsync(source.Token).ConfigureAwait(false);
            try
            {
                var reply = await respond(text, source.Token).ConfigureAwait(false);
                source.Token.ThrowIfCancellationRequested();
                lock (_sync)
                {
                    if (epoch != _epoch) return;
                    publish(reply);
                }
                await speak(reply, source.Token).ConfigureAwait(false);
            }
            finally { _gate.Release(); }
        }
        finally
        {
            lock (_sync) { if (ReferenceEquals(_current, source)) _current = null; }
            source.Dispose();
        }
    }

    public void Interrupt()
    {
        lock (_sync) { _epoch++; _current?.Cancel(); }
    }

    public async ValueTask DisposeAsync()
    {
        Task[] work;
        lock (_sync) { _stop.Cancel(); _epoch++; work = _tasks.ToArray(); }
        try { await Task.WhenAll(work).ConfigureAwait(false); }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    public static bool IsPlaybackEcho(string recognized, string spoken)
    {
        var text = SpeechInputRouter.Normalize(recognized);
        var reply = SpeechInputRouter.Normalize(spoken);
        return text.Length >= 2 && reply.Length >= 2 && reply.Contains(text, StringComparison.Ordinal);
    }
}
