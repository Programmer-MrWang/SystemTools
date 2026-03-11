using Avalonia.Media;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using System;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading;
using SystemTools.Models.ComponentSettings;
using RoutedEventArgs = Avalonia.Interactivity.RoutedEventArgs;

namespace SystemTools.Controls.Components;

[ComponentInfo(
    "8F5E2D1C-3B4A-5678-9ABC-DEF012345678",
    "网络延迟检测",
    "\uEBE0",
    "实时检测网络延迟"
)]
public partial class NetworkStatusComponent : ComponentBase<NetworkStatusSettings>, INotifyPropertyChanged
{
    private string _statusText = "--";
    private IBrush _statusBrush = new SolidColorBrush(Colors.Gray);

    public string StatusText
    {
        get => _statusText;
        set
        {
            _statusText = value;
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public IBrush StatusBrush
    {
        get => _statusBrush;
        set
        {
            _statusBrush = value;
            OnPropertyChanged(nameof(StatusBrush));
        }
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public NetworkStatusComponent()
    {
        InitializeComponent();
    }

    private void NetworkStatusComponent_OnLoaded(object? sender, RoutedEventArgs e)
    {
        NetworkStatusDetectionService.Subscribe(OnStatusUpdated);
        NetworkStatusDetectionService.UpdateSettings(Settings.DetectMode, Settings.PingUrl);
        Settings.PropertyChanged += OnSettingsPropertyChanged;
    }

    private void NetworkStatusComponent_OnUnloaded(object? sender, RoutedEventArgs e)
    {
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
        NetworkStatusDetectionService.Unsubscribe(OnStatusUpdated);
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Settings.DetectMode))
        {
            NetworkStatusDetectionService.UpdateSettings(Settings.DetectMode, Settings.PingUrl);
        }
        
        if (e.PropertyName == nameof(Settings.PingUrl))
        {
            NetworkStatusDetectionService.UpdateSettings(Settings.DetectMode, Settings.PingUrl);
        }
    }

    private void OnStatusUpdated(NetworkStatusResult status)
    {
        StatusText = status.Text;
        StatusBrush = status.Brush;
    }
}

internal static class NetworkStatusDetectionService
{
    private static readonly object SyncLock = new();
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };
    private static readonly DispatcherTimer Timer = new();
    private static readonly SemaphoreSlim CheckSemaphore = new(1, 1);
    private static event Action<NetworkStatusResult>? StatusChanged;

    private static int _subscriberCount;
    private static string _pingUrl = "https://www.baidu.com";
    private static NetworkDetectMode _detectMode = NetworkDetectMode.Auto;
    private static bool _autoModeUseHttp;
    private static int _httpDetectCountSinceIcmp;
    private const int AutoModeIcmpRetryInterval = 60;
    private static NetworkStatusResult _latestResult = new("--", new SolidColorBrush(Colors.Gray));

    static NetworkStatusDetectionService()
    {
        HttpClient.DefaultRequestHeaders.Add("User-Agent", "SystemTools/1.0");
        Timer.Interval = TimeSpan.FromSeconds(1);
        Timer.Tick += async (_, _) => await CheckNetworkStatusAsync();
    }

    public static void Subscribe(Action<NetworkStatusResult> callback)
    {
        lock (SyncLock)
        {
            StatusChanged += callback;
            _subscriberCount++;
            callback(_latestResult);

            if (_subscriberCount == 1)
            {
                Timer.Start();
                _ = CheckNetworkStatusAsync();
            }
        }
    }

    public static void Unsubscribe(Action<NetworkStatusResult> callback)
    {
        lock (SyncLock)
        {
            StatusChanged -= callback;
            _subscriberCount = Math.Max(0, _subscriberCount - 1);

            if (_subscriberCount == 0)
            {
                Timer.Stop();
            }
        }
    }

    public static void UpdateSettings(NetworkDetectMode detectMode, string pingUrl)
    {
        lock (SyncLock)
        {
            _detectMode = detectMode;
            _pingUrl = string.IsNullOrWhiteSpace(pingUrl) ? "https://www.baidu.com" : pingUrl;

            if (detectMode != NetworkDetectMode.Auto)
            {
                _autoModeUseHttp = false;
                _httpDetectCountSinceIcmp = 0;
            }
            else if (!_autoModeUseHttp)
            {
                _httpDetectCountSinceIcmp = 0;
            }
        }

        _ = CheckNetworkStatusAsync();
    }

    private static async Task CheckNetworkStatusAsync()
    {
        await CheckSemaphore.WaitAsync();
        try
        {
            long delay;
            string url;
            NetworkDetectMode mode;
            bool autoModeUseHttp;

            lock (SyncLock)
            {
                url = _pingUrl;
                mode = _detectMode;
                autoModeUseHttp = _autoModeUseHttp;
            }

            NetworkStatusResult result;

            switch (mode)
            {
                case NetworkDetectMode.Icmp:
                    var icmpResult = await TryIcmpPingAsync(url);
                    if (!icmpResult.Success)
                    {
                        PublishResult(new NetworkStatusResult(icmpResult.ErrorText, new SolidColorBrush(Colors.Red)));
                        return;
                    }
                    delay = icmpResult.Delay;
                    break;

                case NetworkDetectMode.Http:
                    delay = await TryHttpPingAsync(url);
                    break;

                case NetworkDetectMode.Auto:
                default:
                    if (!autoModeUseHttp)
                    {
                        var autoIcmpResult = await TryIcmpPingAsync(url);
                        if (autoIcmpResult.Success)
                        {
                            delay = autoIcmpResult.Delay;
                            lock (SyncLock)
                            {
                                _autoModeUseHttp = false;
                                _httpDetectCountSinceIcmp = 0;
                            }
                        }
                        else
                        {
                            lock (SyncLock)
                            {
                                _autoModeUseHttp = true;
                                _httpDetectCountSinceIcmp = 0;
                            }
                            delay = await TryHttpPingAsync(url);
                        }
                    }
                    else
                    {
                        var shouldRetryIcmp = false;
                        lock (SyncLock)
                        {
                            _httpDetectCountSinceIcmp++;
                            if (_httpDetectCountSinceIcmp >= AutoModeIcmpRetryInterval)
                            {
                                _httpDetectCountSinceIcmp = 0;
                                shouldRetryIcmp = true;
                            }
                        }

                        if (shouldRetryIcmp)
                        {
                            var retryIcmpResult = await TryIcmpPingAsync(url);
                            if (retryIcmpResult.Success)
                            {
                                lock (SyncLock)
                                {
                                    _autoModeUseHttp = false;
                                }

                                delay = retryIcmpResult.Delay;
                                break;
                            }
                        }

                        delay = await TryHttpPingAsync(url);
                    }
                    break;
            }

            result = CreateDelayResult(delay);
            PublishResult(result);
        }
        catch (TaskCanceledException)
        {
            PublishResult(new NetworkStatusResult("超时", new SolidColorBrush(Colors.Red)));
        }
        catch (HttpRequestException)
        {
            PublishResult(new NetworkStatusResult("无网络", new SolidColorBrush(Colors.Red)));
        }
        catch
        {
            PublishResult(new NetworkStatusResult("错误", new SolidColorBrush(Colors.Red)));
        }
        finally
        {
            CheckSemaphore.Release();
        }
    }

    private static async Task<IcmpProbeResult> TryIcmpPingAsync(string url)
    {
        try
        {
            var uri = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? new Uri(url)
                : new Uri($"https://{url}");
            var host = uri.Host;

            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 2000);
            
            if (reply.Status == IPStatus.Success)
            {
                if (reply.RoundtripTime is > 0 and < 5)
                {
                    return IcmpProbeResult.Fail("延迟过低");
                }

                if (reply.RoundtripTime <= 0)
                {
                    return IcmpProbeResult.Fail("错误");
                }

                return IcmpProbeResult.Ok(reply.RoundtripTime);
            }

            return reply.Status == IPStatus.TimedOut
                ? IcmpProbeResult.Fail("超时")
                : IcmpProbeResult.Fail("无网络");
        }
        catch (PingException)
        {
            return IcmpProbeResult.Fail("无网络");
        }
        catch
        {
            return IcmpProbeResult.Fail("错误");
        }
        
    }

    private static async Task<long> TryHttpPingAsync(string url)
    {
        var httpUrl = url;
        if (!httpUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) 
            && !httpUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            httpUrl = "https://" + httpUrl;
        }

        var stopwatch = Stopwatch.StartNew();
        
        using var response = await HttpClient.SendAsync(
            new HttpRequestMessage(HttpMethod.Head, httpUrl), 
            HttpCompletionOption.ResponseHeadersRead);
        
        stopwatch.Stop();
        
        response.EnsureSuccessStatusCode();
        return stopwatch.ElapsedMilliseconds;
    }

    private static NetworkStatusResult CreateDelayResult(long delay)
    {
        var brush = delay switch
        {
            < 50 => new SolidColorBrush(Colors.LimeGreen),
            < 100 => new SolidColorBrush(Colors.Green),
            < 300 => new SolidColorBrush(Colors.Orange),
            _ => new SolidColorBrush(Colors.Red)
        };

        return new NetworkStatusResult($"{delay}ms", brush);
    }

    private static void PublishResult(NetworkStatusResult result)
    {
        _latestResult = result;
        Dispatcher.UIThread.Post(() => StatusChanged?.Invoke(result));
    }
}

internal sealed record NetworkStatusResult(string Text, IBrush Brush);

internal sealed record IcmpProbeResult(bool Success, long Delay, string ErrorText)
{
    public static IcmpProbeResult Ok(long delay) => new(true, delay, string.Empty);

    public static IcmpProbeResult Fail(string errorText) => new(false, -1, errorText);
}
