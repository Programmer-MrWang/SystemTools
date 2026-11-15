using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using SystemTools.Contracts;

namespace SystemTools.Common.Media;

public sealed class SpeechInputRouter(ISpeechRecognition backend, CancellationToken stopping) : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly List<KeywordLease> _keywords = [];
    private readonly SemaphoreSlim _foregroundGate = new(1, 1);
    private readonly CancellationTokenSource _stop = CancellationTokenSource.CreateLinkedTokenSource(stopping);
    private CancellationTokenSource? _backgroundStop;
    private Task? _background;
    private Task _foregroundStopped = Task.CompletedTask;
    private bool _foreground;
    public string? LastError { get; private set; }

    public IDisposable RegisterKeyword(string keyword, double threshold, Action matched)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        if (!double.IsFinite(threshold) || threshold < 0 || threshold > 1) throw new ArgumentOutOfRangeException(nameof(threshold));
        var lease = new KeywordLease(this, Normalize(keyword), threshold, matched);
        lock (_sync) { _stop.Token.ThrowIfCancellationRequested(); _keywords.Add(lease); }
        RefreshBackground();
        return lease;
    }

    public void RefreshBackground()
    {
        lock (_sync)
        {
            if (_stop.IsCancellationRequested || _foreground || _background is not null || _keywords.Count == 0) return;
            _backgroundStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            var source = _backgroundStop;
            _background = Task.Run(() => ListenKeywordsAsync(source));
        }
    }

    private async Task ListenKeywordsAsync(CancellationTokenSource source)
    {
        try
        {
            LastError = null;
            await foreach (var segment in backend.RecognizeAsync(source.Token).WithCancellation(source.Token).ConfigureAwait(false))
            {
                if (!segment.IsFinal) continue;
                KeywordLease[] listeners;
                lock (_sync) listeners = _keywords.ToArray();
                foreach (var listener in listeners)
                {
                    Action? callback = null;
                    lock (_sync)
                    {
                        if (!listener.Active || source.IsCancellationRequested ||
                            Stopwatch.GetElapsedTime(listener.LastTriggered) < TimeSpan.FromMilliseconds(700)) continue;
                        if (Score(segment.Text, listener.Keyword) < listener.Threshold) continue;
                        listener.LastTriggered = Stopwatch.GetTimestamp();
                        callback = listener.Matched;
                    }
                    try { callback(); } catch (Exception error) { LastError = error.Message; }
                }
            }
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { }
        catch (Exception error) { LastError = error.Message; }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_backgroundStop, source)) { _backgroundStop = null; _background = null; }
            }
            source.Dispose();
        }
    }

    public async IAsyncEnumerable<SpeechSegment> ListenAsync([EnumeratorCancellation] CancellationToken token)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        if (!await _foregroundGate.WaitAsync(0, stop.Token).ConfigureAwait(false))
            throw new NativeFeatureException(CapabilityState.MissingDevice, "另一个听写或对话正在使用麦克风。");
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? background;
        CancellationTokenSource? backgroundStop;
        lock (_sync)
        {
            _foreground = true;
            _foregroundStopped = ended.Task;
            background = _background;
            backgroundStop = _backgroundStop;
        }
        try
        {
            try { backgroundStop?.Cancel(); } catch (ObjectDisposedException) { }
            if (background is not null) await background.ConfigureAwait(false);
            stop.Token.ThrowIfCancellationRequested();
            await foreach (var segment in backend.RecognizeAsync(stop.Token).WithCancellation(stop.Token).ConfigureAwait(false))
            {
                stop.Token.ThrowIfCancellationRequested();
                yield return segment;
            }
        }
        finally
        {
            lock (_sync) _foreground = false;
            _foregroundGate.Release();
            ended.TrySetResult();
            RefreshBackground();
        }
    }

    private void Remove(KeywordLease lease)
    {
        CancellationTokenSource? stop = null;
        lock (_sync)
        {
            lease.Active = false;
            _keywords.Remove(lease);
            if (_keywords.Count == 0) stop = _backgroundStop;
        }
        try { stop?.Cancel(); } catch (ObjectDisposedException) { }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        Task[] work;
        lock (_sync) work = [_background ?? Task.CompletedTask, _foregroundStopped];
        await Task.WhenAll(work).ConfigureAwait(false);
    }

    public static string Normalize(string value) => string.Concat(value.Normalize(NormalizationForm.FormKC)
        .Where(character => char.IsLetterOrDigit(character))).ToLowerInvariant();

    public static double Score(string text, string keyword)
    {
        var source = Normalize(text);
        var target = Normalize(keyword);
        if (target.Length == 0 || source.Length == 0) return 0;
        if (source.Contains(target, StringComparison.Ordinal)) return 1;
        var previous = Enumerable.Range(0, target.Length + 1).ToArray();
        for (var row = 1; row <= source.Length; row++)
        {
            var current = new int[target.Length + 1]; current[0] = row;
            for (var column = 1; column <= target.Length; column++)
                current[column] = Math.Min(Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + (source[row - 1] == target[column - 1] ? 0 : 1));
            previous = current;
        }
        return 1 - (double)previous[^1] / Math.Max(source.Length, target.Length);
    }

    private sealed class KeywordLease(SpeechInputRouter owner, string keyword, double threshold, Action matched) : IDisposable
    {
        public string Keyword { get; } = keyword;
        public double Threshold { get; } = threshold;
        public Action Matched { get; } = matched;
        public bool Active = true;
        public long LastTriggered;
        public void Dispose() => owner.Remove(this);
    }
}
