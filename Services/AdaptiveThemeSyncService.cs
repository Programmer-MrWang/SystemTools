using Avalonia;
using Avalonia.Threading;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SystemTools.Shared;

namespace SystemTools.Services;

public class AdaptiveThemeSyncService(ILogger<AdaptiveThemeSyncService> logger)
{
    private readonly ILogger<AdaptiveThemeSyncService> _logger = logger;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private int? _lastAppliedTheme;

    public void Start()
    {
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    public void RefreshNow()
    {
        OnTick(this, EventArgs.Empty);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (GlobalConstants.MainConfig?.Data.AutoMatchMainBackgroundTheme != true)
        {
            _lastAppliedTheme = null;
            return;
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        try
        {
            var targetTheme = DetectThemeByMainWindowBackground();
            if (targetTheme == null)
            {
                return;
            }

            var currentTheme = GetCurrentTheme();
            if (targetTheme == _lastAppliedTheme && currentTheme == targetTheme)
            {
                return;
            }

            var themeService = IAppHost.TryGetService<IThemeService>();
            if (themeService == null)
            {
                return;
            }

            themeService.SetTheme(targetTheme.Value, null);
            themeService.CurrentRealThemeMode = targetTheme.Value == 2 ? 1 : 0;
            _lastAppliedTheme = targetTheme;
            RefreshAppearance();
            _logger.LogDebug("已自动匹配主题为：{Theme}", targetTheme == 2 ? "黑暗" : "明亮");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "自动匹配主界面背景色失败，将在下次计时重试。");
        }
    }

    private static int? DetectThemeByMainWindowBackground()
    {
        var handle = Process.GetCurrentProcess().MainWindowHandle;
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        if (!GetWindowRect(handle, out var rect))
        {
            return null;
        }

        var screen = Screen.FromHandle(handle);
        var workingArea = screen.WorkingArea;
        var monitorTopBandHeight = Math.Max(1, workingArea.Height / 5);
        var monitorBandY = IsWindowInTopHalf(rect, workingArea)
            ? workingArea.Top
            : workingArea.Bottom - monitorTopBandHeight;
        var sampleRect = new Rectangle(workingArea.Left, monitorBandY, workingArea.Width, monitorTopBandHeight);

        using var bitmap = new Bitmap(sampleRect.Width, sampleRect.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(sampleRect.Left, sampleRect.Top, 0, 0, sampleRect.Size);

        var width = bitmap.Width;
        var height = bitmap.Height;

        var samples = new (int X, int Y)[]
        {
            (width / 2, height / 2),
            (Math.Max(0, width / 4), Math.Max(0, height / 4)),
            (Math.Max(0, width * 3 / 4), Math.Max(0, height / 4)),
            (Math.Max(0, width / 4), Math.Max(0, height * 3 / 4)),
            (Math.Max(0, width * 3 / 4), Math.Max(0, height * 3 / 4)),
        };

        double luminance = 0;
        foreach (var sample in samples)
        {
            var color = bitmap.GetPixel(Math.Clamp(sample.X, 0, width - 1), Math.Clamp(sample.Y, 0, height - 1));
            luminance += 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
        }
        luminance /= samples.Length;

        return luminance < 128 ? 2 : 1; // 2=黑暗,1=明亮
    }

    private static bool IsWindowInTopHalf(RECT windowRect, Rectangle workingArea)
    {
        var centerY = (windowRect.Top + windowRect.Bottom) / 2;
        return centerY < workingArea.Top + workingArea.Height / 2;
    }

    private static int? GetCurrentTheme()
    {
        var variant = AppBase.Current?.ActualThemeVariant;
        if (variant == null)
        {
            return null;
        }

        if (variant == Avalonia.Styling.ThemeVariant.Dark)
        {
            return 2;
        }

        if (variant == Avalonia.Styling.ThemeVariant.Light)
        {
            return 1;
        }

        return null;
    }

    private static void RefreshAppearance()
    {
        if (Application.Current == null)
        {
            return;
        }

        foreach (var window in Application.Current.Windows)
        {
            window.InvalidateMeasure();
            window.InvalidateArrange();
            window.InvalidateVisual();
        }
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
