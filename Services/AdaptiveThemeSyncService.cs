using Avalonia.Threading;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
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
        var screen = ResolveTargetScreen();
        var captureRect = BuildTargetArea(screen.Bounds, ResolveUseTopAreaFromClassIslandSettings());

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

    private static Rectangle BuildTargetArea(Rectangle screenBounds, bool useTopArea)
    {
        var topHeight = Math.Max(1, screenBounds.Height / 5);
        var bottomY = screenBounds.Bottom - topHeight;

        return useTopArea
            ? new Rectangle(screenBounds.Left, screenBounds.Top, screenBounds.Width, topHeight)
            : new Rectangle(screenBounds.Left, bottomY, screenBounds.Width, topHeight);
    }

    private static Screen ResolveTargetScreen()
    {
        var monitorIndex = ReadClassIslandWindowDockingMonitorIndex();
        if (monitorIndex is int idx && idx >= 0 && idx < Screen.AllScreens.Length)
        {
            return Screen.AllScreens[idx];
        }

        return Screen.PrimaryScreen ?? Screen.AllScreens[0];
    }

    private static bool ResolveUseTopAreaFromClassIslandSettings()
    {
        var dockingLocation = ReadClassIslandWindowDockingLocation();
        if (dockingLocation is null)
        {
            return true;
        }

        // 0=左上角,1=中上侧,2=右上角,3=左下角,4=中下侧,5=右下角
        return dockingLocation is 0 or 1 or 2;
    }

    private static int? ReadClassIslandWindowDockingLocation() =>
        ReadIntFromClassIslandSettings(["windowDockingLocation", "WindowDockingLocation"]);

    private static int? ReadClassIslandWindowDockingMonitorIndex() =>
        ReadIntFromClassIslandSettings(["windowDockingMonitorIndex", "WindowDockingMonitorIndex"]);

    private static int? ReadIntFromClassIslandSettings(string[] keys)
    {
        foreach (var settingsPath in GetPossibleClassIslandSettingsPaths())
        {
            if (!File.Exists(settingsPath))
            {
                continue;
            }

            try
            {
                using var stream = File.OpenRead(settingsPath);
                using var document = JsonDocument.Parse(stream);
                var root = document.RootElement;
                foreach (var key in keys)
                {
                    if (root.TryGetProperty(key, out var value) && value.TryGetInt32(out var intValue))
                    {
                        return intValue;
                    }
                }
            }
            catch
            {
                // Ignore parse errors and try next candidate.
            }
        }

        return null;
    }

    private static string[] GetPossibleClassIslandSettingsPaths()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var processDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return
        [
            Path.Combine(localAppData, "ClassIsland", "Settings.json"),
            Path.Combine(localAppData, "ClassIsland", "Config", "Settings.json"),
            Path.Combine(processDirectory, "Settings.json"),
            Path.Combine(processDirectory, "Config", "Settings.json")
        ];
    }
}
