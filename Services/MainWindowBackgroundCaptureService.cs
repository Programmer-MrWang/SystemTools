using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ClassIsland.Core;
using Microsoft.Extensions.Logging;

namespace SystemTools.Services;

public sealed class MainWindowBackgroundCaptureService(
    MainWindowAreaService mainWindowAreaService,
    ClassIslandSettingsService classIslandSettingsService,
    ILogger<MainWindowBackgroundCaptureService> logger)
{
    private const uint WdaNone = 0x00000000;
    private const uint WdaExcludeFromCapture = 0x00000011;
    private readonly SemaphoreSlim _captureLock = new(1, 1);
    private readonly object _affinityLock = new();
    private readonly object _windowExclusionLock = new();
    private readonly Dictionary<IntPtr, WindowExclusionState> _windowExclusions = new();
    private int _continuousCaptureUsers;
    private int _activeCaptures;
    private IntPtr _affinityWindowHandle;
    private uint _originalAffinity;
    private bool _hasTrackedAffinity;

    public IDisposable BeginContinuousCapture()
    {
        lock (_affinityLock)
        {
            _continuousCaptureUsers++;
        }

        return new ContinuousCaptureLease(this);
    }

    public IDisposable? BeginExcludedWindowCapture(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return null;
        }

        lock (_windowExclusionLock)
        {
            if (_windowExclusions.TryGetValue(windowHandle, out var existing))
            {
                existing.LeaseCount++;
                return new WindowExclusionLease(this, windowHandle);
            }

            if (!GetWindowDisplayAffinity(windowHandle, out var originalAffinity))
            {
                return null;
            }

            var changed = originalAffinity != WdaExcludeFromCapture;
            if (changed && !SetWindowDisplayAffinity(windowHandle, WdaExcludeFromCapture))
            {
                logger.LogDebug(
                    "Unable to exclude a liquid glass window from capture. Error code: {ErrorCode}.",
                    Marshal.GetLastWin32Error());
                return null;
            }

            _windowExclusions[windowHandle] = new WindowExclusionState(
                originalAffinity,
                changed,
                LeaseCount: 1);
            return new WindowExclusionLease(this, windowHandle);
        }
    }

    public async Task<MainWindowBackgroundFrame?> CaptureAsync(CancellationToken cancellationToken)
    {
        await _captureLock.WaitAsync(cancellationToken);
        try
        {
            if (AppBase.Current.MainWindow is not { IsVisible: true } mainWindow)
            {
                return null;
            }

            var captureAreas = ClipToVirtualScreen(mainWindowAreaService.GetLayoutAreas());
            if (captureAreas.Count == 0)
            {
                return null;
            }

            var handle = mainWindow.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero || !GetWindowDisplayAffinity(handle, out var currentAffinity))
            {
                return null;
            }

            bool affinityChanged;
            lock (_affinityLock)
            {
                if (!PrepareCaptureAffinity(handle, currentAffinity, out affinityChanged))
                {
                    return null;
                }

                _activeCaptures++;
            }

            try
            {
                if (affinityChanged)
                {
                    await Task.Delay(50, cancellationToken);
                }

                var regions = new List<MainWindowBackgroundRegion>(captureAreas.Count);
                try
                {
                    foreach (var area in captureAreas)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var bitmap = new Bitmap(area.Width, area.Height);
                        using var graphics = Graphics.FromImage(bitmap);
                        graphics.CopyFromScreen(area.Left, area.Top, 0, 0, area.Size);
                        regions.Add(new MainWindowBackgroundRegion(area, bitmap));
                    }

                    return new MainWindowBackgroundFrame(regions);
                }
                catch
                {
                    foreach (var region in regions)
                    {
                        region.Dispose();
                    }

                    throw;
                }
            }
            finally
            {
                lock (_affinityLock)
                {
                    _activeCaptures--;
                    if (_continuousCaptureUsers == 0)
                    {
                        RestoreCaptureAffinity();
                    }
                }
            }
        }
        finally
        {
            _captureLock.Release();
        }
    }

    public async Task<MainWindowBackgroundFrame?> CaptureAreaAsync(
        Rectangle area,
        IntPtr excludedWindowHandle,
        CancellationToken cancellationToken)
    {
        await _captureLock.WaitAsync(cancellationToken);
        try
        {
            var captureAreas = ClipToVirtualScreen([area]);
            if (captureAreas.Count == 0)
            {
                return null;
            }

            var mainWindowHandle = AppBase.Current.MainWindow?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            var mainAffinityPrepared = false;
            var mainAffinityChanged = false;
            var targetAffinityChanged = false;
            uint targetOriginalAffinity = WdaNone;

            try
            {
                if (mainWindowHandle != IntPtr.Zero &&
                    GetWindowDisplayAffinity(mainWindowHandle, out var currentMainAffinity))
                {
                    lock (_affinityLock)
                    {
                        if (PrepareCaptureAffinity(
                                mainWindowHandle,
                                currentMainAffinity,
                                out mainAffinityChanged))
                        {
                            _activeCaptures++;
                            mainAffinityPrepared = true;
                        }
                    }
                }

                if (excludedWindowHandle != IntPtr.Zero &&
                    excludedWindowHandle != mainWindowHandle)
                {
                    if (!GetWindowDisplayAffinity(excludedWindowHandle, out targetOriginalAffinity))
                    {
                        return null;
                    }

                    if (targetOriginalAffinity != WdaExcludeFromCapture)
                    {
                        if (!SetWindowDisplayAffinity(excludedWindowHandle, WdaExcludeFromCapture))
                        {
                            return null;
                        }

                        targetAffinityChanged = true;
                    }
                }

                if (mainAffinityChanged || targetAffinityChanged)
                {
                    await Task.Delay(50, cancellationToken);
                }

                var regions = new List<MainWindowBackgroundRegion>(captureAreas.Count);
                try
                {
                    foreach (var captureArea in captureAreas)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var bitmap = new Bitmap(captureArea.Width, captureArea.Height);
                        using var graphics = Graphics.FromImage(bitmap);
                        graphics.CopyFromScreen(
                            captureArea.Left,
                            captureArea.Top,
                            0,
                            0,
                            captureArea.Size);
                        regions.Add(new MainWindowBackgroundRegion(captureArea, bitmap));
                    }

                    return new MainWindowBackgroundFrame(regions);
                }
                catch
                {
                    foreach (var region in regions)
                    {
                        region.Dispose();
                    }

                    throw;
                }
            }
            finally
            {
                if (targetAffinityChanged &&
                    !SetWindowDisplayAffinity(excludedWindowHandle, targetOriginalAffinity))
                {
                    logger.LogDebug(
                        "Unable to restore the AI chat window capture affinity. Error code: {ErrorCode}.",
                        Marshal.GetLastWin32Error());
                }

                if (mainAffinityPrepared)
                {
                    lock (_affinityLock)
                    {
                        _activeCaptures--;
                        if (_continuousCaptureUsers == 0)
                        {
                            RestoreCaptureAffinity();
                        }
                    }
                }
            }
        }
        finally
        {
            _captureLock.Release();
        }
    }

    private bool PrepareCaptureAffinity(IntPtr handle, uint currentAffinity, out bool affinityChanged)
    {
        affinityChanged = false;
        if (!_hasTrackedAffinity || _affinityWindowHandle != handle)
        {
            RestoreCaptureAffinity();
            _affinityWindowHandle = handle;
            _originalAffinity = currentAffinity;
            _hasTrackedAffinity = true;
        }

        if (currentAffinity == WdaExcludeFromCapture)
        {
            return true;
        }

        if (!SetWindowDisplayAffinity(handle, WdaExcludeFromCapture))
        {
            logger.LogDebug(
                "Unable to exclude the ClassIsland main window from capture. Error code: {ErrorCode}.",
                Marshal.GetLastWin32Error());
            ClearAffinityTracking();
            return false;
        }

        affinityChanged = true;
        return true;
    }

    private void EndContinuousCapture()
    {
        lock (_affinityLock)
        {
            if (_continuousCaptureUsers == 0)
            {
                return;
            }

            _continuousCaptureUsers--;
            if (_continuousCaptureUsers == 0 && _activeCaptures == 0)
            {
                RestoreCaptureAffinity();
            }
        }
    }

    private void EndExcludedWindowCapture(IntPtr windowHandle)
    {
        lock (_windowExclusionLock)
        {
            if (!_windowExclusions.TryGetValue(windowHandle, out var state))
            {
                return;
            }

            state.LeaseCount--;
            if (state.LeaseCount > 0)
            {
                return;
            }

            _windowExclusions.Remove(windowHandle);
            if (state.Changed &&
                !SetWindowDisplayAffinity(windowHandle, state.OriginalAffinity))
            {
                logger.LogDebug(
                    "Unable to restore a liquid glass window capture affinity. Error code: {ErrorCode}.",
                    Marshal.GetLastWin32Error());
            }
        }
    }

    private void RestoreCaptureAffinity()
    {
        if (!_hasTrackedAffinity)
        {
            return;
        }

        var targetAffinity = classIslandSettingsService.GetWindowCaptureBlockingEnabled() switch
        {
            true => WdaExcludeFromCapture,
            false => WdaNone,
            null => _originalAffinity
        };
        if (!SetWindowDisplayAffinity(_affinityWindowHandle, targetAffinity))
        {
            logger.LogWarning(
                "Failed to restore the ClassIsland main window capture affinity. Error code: {ErrorCode}.",
                Marshal.GetLastWin32Error());
        }

        ClearAffinityTracking();
    }

    private void ClearAffinityTracking()
    {
        _affinityWindowHandle = IntPtr.Zero;
        _originalAffinity = WdaNone;
        _hasTrackedAffinity = false;
    }

    private static List<Rectangle> ClipToVirtualScreen(IReadOnlyList<Rectangle> areas)
    {
        var virtualScreen = System.Windows.Forms.SystemInformation.VirtualScreen;
        var result = new List<Rectangle>(areas.Count);
        foreach (var area in areas)
        {
            var clipped = Rectangle.Intersect(area, virtualScreen);
            if (clipped.Width > 0 && clipped.Height > 0)
            {
                result.Add(clipped);
            }
        }

        return result;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);

    private sealed class ContinuousCaptureLease(MainWindowBackgroundCaptureService owner) : IDisposable
    {
        private MainWindowBackgroundCaptureService? _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.EndContinuousCapture();
        }
    }

    private sealed class WindowExclusionLease(
        MainWindowBackgroundCaptureService owner,
        IntPtr windowHandle) : IDisposable
    {
        private MainWindowBackgroundCaptureService? _owner = owner;

        public void Dispose()
            => Interlocked.Exchange(ref _owner, null)?.EndExcludedWindowCapture(windowHandle);
    }

    private sealed class WindowExclusionState(
        uint originalAffinity,
        bool changed,
        int LeaseCount)
    {
        public uint OriginalAffinity { get; } = originalAffinity;
        public bool Changed { get; } = changed;
        public int LeaseCount { get; set; } = LeaseCount;
    }
}

public sealed class MainWindowBackgroundFrame(IReadOnlyList<MainWindowBackgroundRegion> regions) : IDisposable
{
    public IReadOnlyList<MainWindowBackgroundRegion> Regions { get; } = regions;

    public void Dispose()
    {
        foreach (var region in Regions)
        {
            region.Dispose();
        }
    }
}

public sealed class MainWindowBackgroundRegion(Rectangle area, Bitmap bitmap) : IDisposable
{
    public Rectangle Area { get; } = area;
    public Bitmap Bitmap { get; } = bitmap;

    public void Dispose() => Bitmap.Dispose();
}
