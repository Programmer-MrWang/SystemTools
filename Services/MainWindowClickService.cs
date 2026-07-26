using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClassIsland.Core;
using Microsoft.Extensions.Logging;

namespace SystemTools.Services;

public sealed class MainWindowClickService(ILogger<MainWindowClickService> logger) : IDisposable
{
    private const int WhMouseLl = 14;
    private const uint WmLButtonDown = 0x0201;
    private const uint LlmhfInjected = 0x00000001;

    private readonly object _syncRoot = new();
    private readonly HashSet<EventHandler> _handlers = [];
    private LowLevelMouseProc? _mouseProc;
    private IntPtr _mouseHook;

    public void Subscribe(EventHandler handler)
    {
        lock (_syncRoot)
        {
            if (!_handlers.Add(handler) || _handlers.Count != 1)
            {
                return;
            }

            InstallHook();
        }
    }

    public void Unsubscribe(EventHandler handler)
    {
        lock (_syncRoot)
        {
            _handlers.Remove(handler);
            if (_handlers.Count == 0)
            {
                RemoveHook();
            }
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            _handlers.Clear();
            RemoveHook();
        }
    }

    private void InstallHook()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            return;
        }

        _mouseProc ??= OnLowLevelMouse;
        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProc, IntPtr.Zero, 0);
        if (_mouseHook == IntPtr.Zero)
        {
            logger.LogError(new Win32Exception(Marshal.GetLastWin32Error()), "无法监听主界面点击");
        }
    }

    private void RemoveHook()
    {
        if (_mouseHook == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_mouseHook);
        _mouseHook = IntPtr.Zero;
    }

    private IntPtr OnLowLevelMouse(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && lParam != IntPtr.Zero && unchecked((uint)wParam.ToInt64()) == WmLButtonDown)
        {
            var info = Marshal.PtrToStructure<MsllHookStruct>(lParam);
            if ((info.Flags & LlmhfInjected) == 0)
            {
                var point = new PixelPoint(info.Point.X, info.Point.Y);
                Dispatcher.UIThread.Post(() => ProcessLeftButtonDown(point));
            }
        }

        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void ProcessLeftButtonDown(PixelPoint point)
    {
        if (!IsInMainWindowArea(point))
        {
            return;
        }

        EventHandler[] handlers;
        lock (_syncRoot)
        {
            handlers = _handlers.ToArray();
        }

        foreach (var handler in handlers)
        {
            handler(this, EventArgs.Empty);
        }
    }

    private static bool IsInMainWindowArea(PixelPoint point)
    {
        if (AppBase.Current.MainWindow is not { IsVisible: true } window)
        {
            return false;
        }

        try
        {
            return window.GetVisualDescendants()
                .OfType<Grid>()
                .Where(grid => grid.Name == "PART_GridWrapper" && grid.IsEffectivelyVisible)
                .Any(grid => ContainsScreenPoint(grid, point));
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool ContainsScreenPoint(Control control, PixelPoint point)
    {
        if (control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
        {
            return false;
        }

        var topLeft = control.PointToScreen(new Avalonia.Point(0, 0));
        var bottomRight = control.PointToScreen(new Avalonia.Point(control.Bounds.Width, control.Bounds.Height));
        var left = Math.Min(topLeft.X, bottomRight.X);
        var top = Math.Min(topLeft.Y, bottomRight.Y);
        var right = Math.Max(topLeft.X, bottomRight.X);
        var bottom = Math.Max(topLeft.Y, bottomRight.Y);

        return point.X >= left && point.X <= right && point.Y >= top && point.Y <= bottom;
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsllHookStruct
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);
}
