using System.Runtime.InteropServices;
using SystemTools.Contracts;

namespace SystemTools.Platforms.Linux;

public sealed class HotkeysService : IGlobalHotkeys, IDisposable
{
    private readonly List<Registration> _registrations = [];
    private readonly object _gate = new();
    private bool _disposed;
    public Capability Probe()
    {
        try { using var connection = new X11Connection(); return new(CapabilityState.Available); }
        catch (Exception error) when (error is DllNotFoundException or NativeFeatureException) { return new(CapabilityState.MissingDependency, "需要原生 X11 热键服务。"); }
    }
    public IDisposable Register(string key, int modifiers, Action pressed)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var registration = new Registration(key, modifiers, pressed); _registrations.Add(registration); return registration;
        }
    }
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; foreach (var item in _registrations) item.Dispose(); _registrations.Clear(); }
    }
    private sealed class Registration : IDisposable
    {
        private readonly X11Connection _connection = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly int _key;
        private readonly uint _modifiers;
        private readonly Task _poll;
        private int _disposed;
        internal Registration(string key, int modifiers, Action pressed)
        {
            var symbol = StringToKeysym(KeyboardService.Symbol(key));
            _key = KeysymToKeycode(_connection.Display, symbol);
            _modifiers = (uint)(((modifiers & 1) != 0 ? 8 : 0) | ((modifiers & 2) != 0 ? 4 : 0) | ((modifiers & 4) != 0 ? 1 : 0) | ((modifiers & 8) != 0 ? 64 : 0));
            try
            {
                if (_key == 0) throw new NativeFeatureException(CapabilityState.Unsupported, "当前键盘布局不支持此按键。");
                X11ErrorTrap.Run(_connection.Display, () =>
                {
                    foreach (var extra in new uint[] { 0, 2, 16, 18 }) GrabKey(_connection.Display, _key, _modifiers | extra, X11Connection.Root(_connection.Display), 0, 1, 1);
                });
            }
            catch { _connection.Dispose(); throw; }
            _poll = Poll(pressed);
        }
        private async Task Poll(Action pressed)
        {
            var buffer = Marshal.AllocHGlobal(192);
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    while (Pending(_connection.Display) != 0)
                    {
                        NextEvent(_connection.Display, buffer);
                        var type = Marshal.ReadInt32(buffer);
                        var state = (uint)Marshal.ReadInt32(buffer, 80);
                        var code = Marshal.ReadInt32(buffer, 84);
                        if (type == 2 && code == _key && (state & ~18u) == _modifiers && !_stop.IsCancellationRequested) pressed();
                    }
                    await Task.Delay(20, _stop.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _stop.Cancel();
            _poll.GetAwaiter().GetResult();
            foreach (var extra in new uint[] { 0, 2, 16, 18 }) UngrabKey(_connection.Display, _key, _modifiers | extra, X11Connection.Root(_connection.Display));
            Sync(_connection.Display, 0); _connection.Dispose(); _stop.Dispose();
        }
    }
    [DllImport(X11Connection.Library, EntryPoint = "XStringToKeysym")] private static extern nuint StringToKeysym([MarshalAs(UnmanagedType.LPUTF8Str)] string key);
    [DllImport(X11Connection.Library, EntryPoint = "XKeysymToKeycode")] private static extern byte KeysymToKeycode(nint display, nuint symbol);
    [DllImport(X11Connection.Library, EntryPoint = "XGrabKey")] private static extern void GrabKey(nint display, int key, uint modifiers, nuint window, int owner, int pointerMode, int keyboardMode);
    [DllImport(X11Connection.Library, EntryPoint = "XUngrabKey")] private static extern void UngrabKey(nint display, int key, uint modifiers, nuint window);
    [DllImport(X11Connection.Library, EntryPoint = "XPending")] private static extern int Pending(nint display);
    [DllImport(X11Connection.Library, EntryPoint = "XNextEvent")] private static extern int NextEvent(nint display, nint buffer);
    [DllImport(X11Connection.Library, EntryPoint = "XSync")] internal static extern int Sync(nint display, int discard);
}

internal static class X11ErrorTrap
{
    private static readonly object Gate = new();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ErrorHandler(nint display, nint error);
    internal static void Run(nint display, Action operation)
    {
        lock (Gate)
        {
            HotkeysService.Sync(display, 0);
            var failed = false;
            nint previous = 0;
            ErrorHandler handler = (source, error) =>
            {
                if (source == display) { failed = true; return 0; }
                return previous == 0 ? 0 : Marshal.GetDelegateForFunctionPointer<ErrorHandler>(previous)(source, error);
            };
            previous = SetErrorHandler(Marshal.GetFunctionPointerForDelegate(handler));
            try { operation(); HotkeysService.Sync(display, 0); }
            finally { SetErrorHandler(previous); GC.KeepAlive(handler); }
            if (failed) throw new NativeFeatureException(CapabilityState.Unsupported, "热键已被其他应用占用或 X11 拒绝注册，请更换组合键。");
        }
    }
    [DllImport(X11Connection.Library, EntryPoint = "XSetErrorHandler")] private static extern nint SetErrorHandler(nint handler);
}
