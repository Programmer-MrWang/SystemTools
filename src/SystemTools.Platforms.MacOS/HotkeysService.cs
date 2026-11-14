using SystemTools.Contracts;

namespace SystemTools.Platforms.MacOS;

public sealed class HotkeysService : IGlobalHotkeys, IDisposable
{
    private readonly object _gate = new();
    private readonly List<Registration> _registrations = [];
    private bool _disposed;
    public Capability Probe() => NativeBridge.Probe("hotkey.probe");
    public IDisposable Register(string key, int modifiers, Action pressed)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var id = NativeBridge.Require(new { operation = "hotkey.register", key, modifiers }).GetProperty("id").GetInt32();
            var registration = new Registration(id, pressed);
            _registrations.Add(registration); return registration;
        }
    }
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; foreach (var item in _registrations) item.Dispose(); _registrations.Clear(); }
    }
    private sealed class Registration : IDisposable
    {
        private readonly int _id;
        private readonly CancellationTokenSource _stop = new();
        private int _disposed;
        internal Registration(int id, Action pressed) { _id = id; _ = Poll(pressed); }
        private async Task Poll(Action pressed)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
            try
            {
                while (await timer.WaitForNextTickAsync(_stop.Token))
                {
                    var count = NativeBridge.Require(new { operation = "hotkey.take", id = _id }).GetProperty("count").GetInt32();
                    if (count > 0 && !_stop.IsCancellationRequested) pressed();
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (Exception) when (_stop.IsCancellationRequested) { }
            catch (Exception error)
            {
                System.Diagnostics.Trace.TraceError("SystemTools hotkey monitoring stopped: {0}", error);
                Dispose();
            }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _stop.Cancel(); NativeBridge.Require(new { operation = "hotkey.unregister", id = _id });
        }
    }
}
