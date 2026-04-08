using Avalonia.Threading;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ClassIsland.Shared;
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
            return;
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        try
        {
            var targetTheme = DetectThemeByScreenBackground();
            if (targetTheme == null || targetTheme == _lastAppliedTheme)
            {
                return;
            }

            var themeService = IAppHost.TryGetService<IThemeService>();
            if (themeService == null)
            {
                return;
            }

            themeService.SetTheme(targetTheme.Value, null);
            _lastAppliedTheme = targetTheme;
            _logger.LogDebug("已自动匹配主题为：{Theme}", targetTheme == 1 ? "黑暗" : "明亮");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "自动匹配主界面背景色失败，将在下次计时重试。");
        }
    }

    private static int? DetectThemeByScreenBackground()
    {
        var handle = Process.GetCurrentProcess().MainWindowHandle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var rect))
        {
            return null;
        }

        var mainWindow = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        var screen = Screen.FromRectangle(mainWindow);
        var captureRect = BuildTargetArea(screen.Bounds, mainWindow);

        using var bitmap = new Bitmap(captureRect.Width, captureRect.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(captureRect.Left, captureRect.Top, 0, 0, captureRect.Size);

        double luminance = 0;
        long samples = 0;
        const int grid = 8;
        var stepX = Math.Max(1, captureRect.Width / grid);
        var stepY = Math.Max(1, captureRect.Height / grid);

        for (var y = 0; y < captureRect.Height; y += stepY)
        {
            for (var x = 0; x < captureRect.Width; x += stepX)
            {
                var color = bitmap.GetPixel(Math.Clamp(x, 0, captureRect.Width - 1), Math.Clamp(y, 0, captureRect.Height - 1));
                luminance += 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
                samples++;
            }
        }

        if (samples == 0)
        {
            return null;
        }

        luminance /= samples;

        // 与 ClassIsland 的主题模式保持一致：0=明亮，1=黑暗。
        return luminance < 128 ? 1 : 0;
    }

    private static Rectangle BuildTargetArea(Rectangle screenBounds, Rectangle mainWindow)
    {
        var topHeight = Math.Max(1, screenBounds.Height / 5);
        var bottomY = screenBounds.Bottom - topHeight;

        var isTop = mainWindow.Top + mainWindow.Height / 2 <= screenBounds.Top + screenBounds.Height / 2;
        return isTop
            ? new Rectangle(screenBounds.Left, screenBounds.Top, screenBounds.Width, topHeight)
            : new Rectangle(screenBounds.Left, bottomY, screenBounds.Width, topHeight);
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
