using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using SystemTools.Contracts;

namespace SystemTools;

public sealed class MediaPlaybackMonitor(PlatformMediaServices media) : IHostedService
{
    private CancellationTokenSource? _stop;
    private Task _task = Task.CompletedTask;
    private IReadOnlyList<MediaSession> _sessions = [];
    private long _updated;
    public IReadOnlyList<MediaSession> Current => Stopwatch.GetElapsedTime(Interlocked.Read(ref _updated)) <= TimeSpan.FromSeconds(5)
        ? Volatile.Read(ref _sessions) : [];
    public bool IsPlaying => Current.Any(session => session.State == "Playing");
    public string? LastError { get; private set; }
    public Task StartAsync(CancellationToken token)
    {
        _stop = CancellationTokenSource.CreateLinkedTokenSource(token, media.Stopping);
        _task = Task.Run(() => MonitorAsync(_stop.Token));
        return Task.CompletedTask;
    }
    private async Task MonitorAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var sessions = media.Media.Probe().CanExecute ? await media.Media.ReadAsync(token).ConfigureAwait(false) : [];
                    Volatile.Write(ref _sessions, sessions); LastError = null;
                    Interlocked.Exchange(ref _updated, Stopwatch.GetTimestamp());
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception error) { Volatile.Write(ref _sessions, []); LastError = error.Message; }
                await Task.Delay(750, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { Volatile.Write(ref _sessions, []); }
    }
    public async Task StopAsync(CancellationToken token)
    {
        _stop?.Cancel();
        await _task.ConfigureAwait(false);
        _stop?.Dispose();
    }
}
