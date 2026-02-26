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
    private string _statusText = "--";
    private IBrush _statusBrush = new SolidColorBrush(Colors.Gray);
    private bool _autoModeUseHttp = false;

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
        _timer = new DispatcherTimer();
        _timer.Tick += OnTimer_Ticked;
        
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "SystemTools/1.0");
    }

    private void NetworkStatusComponent_OnLoaded(object? sender, RoutedEventArgs e)
    {
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Start();
        
        Settings.PropertyChanged += OnSettingsPropertyChanged;
        
        CheckNetworkStatus();
    }

    private void NetworkStatusComponent_OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _timer.Stop();
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
        _httpClient?.Dispose();
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Settings.DetectMode))
        {
            _autoModeUseHttp = false;
            CheckNetworkStatus();
        }
        
        if (e.PropertyName == nameof(Settings.PingUrl))
        {
            CheckNetworkStatus();
        }
    }

    private void OnTimer_Ticked(object? sender, EventArgs e)
    {
        CheckNetworkStatus();
    }

    private async void CheckNetworkStatus()
    {
        try
        {
            var url = Settings.PingUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                url = "https://www.baidu.com";
            }

            long delay;

            switch (Settings.DetectMode)
            {
                case NetworkDetectMode.Icmp:
                    delay = await TryIcmpPingAsync(url);
                    if (delay <= 0)
                    {
                        StatusText = "失败";
                        StatusBrush = new SolidColorBrush(Colors.Red);
                        return;
                    }
                    break;

                case NetworkDetectMode.Http:
                    delay = await TryHttpPingAsync(url);
                    break;

                case NetworkDetectMode.Auto:
                default:
                    if (!_autoModeUseHttp)
                    {
                        var icmpDelay = await TryIcmpPingAsync(url);
                        if (icmpDelay > 0)
                        {
                            delay = icmpDelay;
                        }
                        else
                        {
                            _autoModeUseHttp = true;
                            delay = await TryHttpPingAsync(url);
                        }
                    }
                    else
                    {
                        delay = await TryHttpPingAsync(url);
                    }
                    break;
            }

            UpdateStatus(delay);
        }
        catch (TaskCanceledException)
        {
            StatusText = "超时";
            StatusBrush = new SolidColorBrush(Colors.Red);
        }
        catch (HttpRequestException)
        {
            StatusText = "无网络";
            StatusBrush = new SolidColorBrush(Colors.Red);
        }
        catch
        {
            StatusText = "错误";
            StatusBrush = new SolidColorBrush(Colors.Red);
        }
    }

    private async Task<long> TryIcmpPingAsync(string url)
    {
        try
        {
            var uri = new Uri(url);
            var host = uri.Host;

            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 2000);
            
            if (reply.Status == IPStatus.Success && reply.RoundtripTime > 0)
            {
                return reply.RoundtripTime;
            }
        }
        catch { }
        
        return -1;
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
}