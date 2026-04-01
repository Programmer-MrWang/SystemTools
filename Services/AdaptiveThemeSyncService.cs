using Avalonia.Threading;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
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
            var targetTheme = DetectThemeByMainWindowBackground();
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

        var width = Math.Max(1, rect.Right - rect.Left);
        var height = Math.Max(1, rect.Bottom - rect.Top);

        using var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height));

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
