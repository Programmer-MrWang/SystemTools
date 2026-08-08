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

            var luminance = BackgroundLuminanceCalculator.CalculateAverage(frame);
            if (luminance == null)
            {
                return;
            }

            var targetTheme = luminance < BackgroundLuminanceCalculator.DarkThreshold ? DarkTheme : LightTheme;
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

}
