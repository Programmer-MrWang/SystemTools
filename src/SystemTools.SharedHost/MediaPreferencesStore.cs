using SystemTools.Common.Storage;
using SystemTools.Contracts;

namespace SystemTools;

public sealed class MediaPreferencesStore
{
    private readonly AtomicJsonStore<MediaPreferences> _store;
    public MediaPreferences Current { get; }
    public MediaPreferencesStore(string folder)
    {
        _store = new(Path.Combine(folder, "Media.json"));
        Current = _store.Load();
    }
    public void Save() => _store.Save(Current);
}

public sealed class CameraCaptureContext(ICameraCapture camera) : IDisposable
{
    public ICameraCapture Camera { get; } = camera;
    private readonly CancellationTokenSource _stop = new();
    private int _disposed;
    public CancellationToken Stopping => _stop.Token;
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
    }
}
