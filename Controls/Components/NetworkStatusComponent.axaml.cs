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
    private readonly DispatcherTimer _timer;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _checkSemaphore = new(1, 1);

    private string _statusText = "--";
    private IBrush _statusBrush = new SolidColorBrush(Colors.Gray);
    private bool _autoModeForceHttpUntilRestart;

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

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += OnTimerTicked;

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "SystemTools/1.0");
    }

    private void NetworkStatusComponent_OnLoaded(object? sender, RoutedEventArgs e)
    {
        Settings.PropertyChanged += OnSettingsPropertyChanged;
        _timer.Start();
        _ = CheckNetworkStatusAsync();
    }

    private void NetworkStatusComponent_OnUnloaded(object? sender, RoutedEventArgs e)
    {
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
        _timer.Stop();
        _httpClient.Dispose();
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Settings.DetectMode))
        {
            _ = CheckNetworkStatusAsync();
            return;
        }

        if (e.PropertyName == nameof(Settings.PingUrl))
        {
            _ = CheckNetworkStatusAsync();
        }
    }

    private void OnTimerTicked(object? sender, EventArgs e)
    {
        _ = CheckNetworkStatusAsync();
    }

    private async Task CheckNetworkStatusAsync()
    {
        if (!await _checkSemaphore.WaitAsync(0))
        {
            return;
        }

        try
        {
            var url = string.IsNullOrWhiteSpace(Settings.PingUrl)
                ? "https://www.baidu.com"
                : Settings.PingUrl;

            long delay;
            switch (Settings.DetectMode)
            {
                case NetworkDetectMode.Icmp:
                {
                    var icmpResult = await TryIcmpPingAsync(url);
                    if (!icmpResult.Success)
                    {
                        SetErrorStatus(icmpResult.ErrorText);
                        return;
                    }

                    delay = icmpResult.Delay;
                    break;
                }
                case NetworkDetectMode.Http:
                    delay = await TryHttpPingAsync(url);
                    break;
                case NetworkDetectMode.Auto:
                default:
                    if (!_autoModeForceHttpUntilRestart)
                    {
                        var autoIcmpResult = await TryIcmpPingAsync(url);
                        if (autoIcmpResult.Success)
                        {
                            delay = autoIcmpResult.Delay;
                            break;
                        }

                        _autoModeForceHttpUntilRestart = true;
                    }

                    delay = await TryHttpPingAsync(url);
                    break;
            }

            UpdateStatus(delay);
        }
        catch (TaskCanceledException)
        {
            SetErrorStatus("超时");
        }
        catch (HttpRequestException)
        {
            SetErrorStatus("超时");
        }
        catch
        {
            SetErrorStatus("错误");
        }
        finally
        {
            _checkSemaphore.Release();
        }
    }

    private async Task<IcmpProbeResult> TryIcmpPingAsync(string url)
    {
        try
        {
            var uri = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? new Uri(url)
                : new Uri($"https://{url}");

            using var ping = new Ping();
            var reply = await ping.SendPingAsync(uri.Host, 2000);

            if (reply.Status == IPStatus.Success)
            {
                if (reply.RoundtripTime <= 0)
                {
                    return IcmpProbeResult.Fail("0ms");
                }

                return IcmpProbeResult.Ok(reply.RoundtripTime);
            }

            return reply.Status == IPStatus.TimedOut
                ? IcmpProbeResult.Fail("超时")
                : IcmpProbeResult.Fail("超时");
        }
        catch (PingException)
        {
            return IcmpProbeResult.Fail("超时");
        }
        catch
        {
            return IcmpProbeResult.Fail("错误");
        }
    }

    private async Task<long> TryHttpPingAsync(string url)
    {
        var httpUrl = url;
        if (!httpUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !httpUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            httpUrl = "https://" + httpUrl;
        }

        var stopwatch = Stopwatch.StartNew();

        using var response = await _httpClient.SendAsync(
            new HttpRequestMessage(HttpMethod.Head, httpUrl),
            HttpCompletionOption.ResponseHeadersRead);

        stopwatch.Stop();
        response.EnsureSuccessStatusCode();

        return stopwatch.ElapsedMilliseconds;
    }

    private void UpdateStatus(long delay)
    {
        StatusText = $"{delay}ms";
        StatusBrush = delay switch
        {
            < 50 => new SolidColorBrush(Colors.LimeGreen),
            < 100 => new SolidColorBrush(Colors.Green),
            < 300 => new SolidColorBrush(Colors.Orange),
            _ => new SolidColorBrush(Colors.Red)
        };
    }

    private void SetErrorStatus(string text)
    {
        StatusText = text;
        StatusBrush = new SolidColorBrush(Colors.Red);
    }

    private sealed record IcmpProbeResult(bool Success, long Delay, string ErrorText)
    {
        public static IcmpProbeResult Ok(long delay) => new(true, delay, string.Empty);

        public static IcmpProbeResult Fail(string errorText) => new(false, -1, errorText);
    }
}
