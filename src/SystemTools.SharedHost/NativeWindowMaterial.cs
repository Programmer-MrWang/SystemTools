using Avalonia.Controls;
using Avalonia.Media;
using SystemTools.Contracts;
using Avalonia.Threading;

namespace SystemTools;

/// <summary>Owns material only on plugin windows; closes restore native view ownership before releasing the window.</summary>
public sealed class NativeWindowMaterial(NativeDesktopServices native) : IDisposable
{
    private readonly Dictionary<Window, (int Style, IDisposable? Lease)> _windows = [];
    public string LastError { get; private set; } = "";
    private bool _disposed;
    public void Update(Window window, int style)
    {
        if (_disposed || native.Stopping.IsCancellationRequested) return;
        if (_windows.TryGetValue(window, out var state) && state.Style == style) return;
        var known = _windows.Remove(window, out state);
        state.Lease?.Dispose();
        if (!known) window.Closed += Closed;
        IDisposable? lease = null;
        try
        {
            window.Background = style == 0 ? null : Brushes.Transparent;
            if (style != 0 && native.Advanced.Material is { } material && window.TryGetPlatformHandle() is { } handle)
                lease = material.Apply(handle.Handle, DesktopMaterial.SystemGlass, result => LastError = result.Reason);
            else if (style != 0)
            {
                window.Background = window.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? new SolidColorBrush(Color.FromRgb(32, 32, 32)) : Brushes.White;
                LastError = "当前平台没有经过验证的系统玻璃后端，使用实色表面。";
            }
        }
        catch (Exception error)
        {
            window.Background = window.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? new SolidColorBrush(Color.FromRgb(32, 32, 32)) : Brushes.White;
            LastError = error.Message;
        }
        _windows[window] = (style, lease);
    }
    private void Closed(object? sender, EventArgs args)
    {
        if (sender is not Window window) return;
        window.Closed -= Closed;
        if (_windows.Remove(window, out var state)) state.Lease?.Dispose();
    }
    public void Dispose()
    {
        void Release()
        {
            if (_disposed) return;
            _disposed = true;
            List<Exception> failures = [];
            foreach (var (window, state) in _windows)
            {
                window.Closed -= Closed;
                try { state.Lease?.Dispose(); } catch (Exception error) { failures.Add(error); }
            }
            _windows.Clear();
            if (failures.Count != 0) throw new AggregateException("部分材质租约未能释放。", failures);
        }
        if (Dispatcher.UIThread.CheckAccess()) Release(); else Dispatcher.UIThread.Post(Release);
    }
}
