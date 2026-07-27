using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SystemTools.ConfigHandlers;

namespace SystemTools.Services;

public sealed class AdaptiveThemeSyncService(
    MainConfigHandler configHandler,
    MainWindowBackgroundCaptureService backgroundCaptureService,
    ClassIslandSettingsService classIslandSettingsService,
    ILogger<AdaptiveThemeSyncService> logger)
{
    private const int LightTheme = 1;
    private const int DarkTheme = 2;
    private const double DarkLuminanceThreshold = 128;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly SemaphoreSlim _captureLock = new(1, 1);
    private CancellationTokenSource? _cancellationTokenSource;
    private IDisposable? _continuousCaptureLease;

    public void Start()
    {
        _timer.Tick -= OnTimerTick;
        _timer.Tick += OnTimerTick;
        ApplyConfig();
    }

    public void Stop()
    {
        _timer.Stop();
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
        _continuousCaptureLease?.Dispose();
        _continuousCaptureLease = null;
    }

    public void ApplyConfig()
    {
        Stop();
        if (!configHandler.Data.AutoSwitchClassIslandTheme || !OperatingSystem.IsWindows())
        {
            return;
        }

        _continuousCaptureLease = backgroundCaptureService.BeginContinuousCapture();
        _cancellationTokenSource = new CancellationTokenSource();
        _timer.Start();
        _ = RefreshNowAsync(_cancellationTokenSource.Token);
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        if (_cancellationTokenSource is not { } source)
        {
            return;
        }

        await RefreshNowAsync(source.Token);
    }

    private async Task RefreshNowAsync(CancellationToken cancellationToken)
    {
        if (!await _captureLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            using var frame = await backgroundCaptureService.CaptureAsync(cancellationToken);
            if (frame == null || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var luminance = CalculateAverageLuminance(frame);
            if (luminance == null)
            {
                return;
            }

            var targetTheme = luminance < DarkLuminanceThreshold ? DarkTheme : LightTheme;
            if (classIslandSettingsService.SetTheme(targetTheme))
            {
                logger.LogDebug("主界面背后区域平均亮度为 {Luminance:F1}，已匹配为{Theme}主题。",
                    luminance, targetTheme == DarkTheme ? "黑暗" : "明亮");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "自动切换 ClassIsland 主题失败，将在下次计时重试。");
        }
        finally
        {
            _captureLock.Release();
        }
    }

    private static double? CalculateAverageLuminance(MainWindowBackgroundFrame frame)
    {
        double totalLuminance = 0;
        long sampleCount = 0;

        foreach (var region in frame.Regions)
        {
            var bitmap = region.Bitmap;

            const int sampleStep = 8;
            for (var y = 0; y < bitmap.Height; y += sampleStep)
            {
                for (var x = 0; x < bitmap.Width; x += sampleStep)
                {
                    var color = bitmap.GetPixel(x, y);
                    totalLuminance += 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
                    sampleCount++;
                }
            }
        }

        return sampleCount == 0 ? null : totalLuminance / sampleCount;
    }

}
