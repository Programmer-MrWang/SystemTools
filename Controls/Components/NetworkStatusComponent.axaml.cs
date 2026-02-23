using Avalonia.Media;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using System;
using System.ComponentModel;
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
        _timer = new DispatcherTimer();
        _timer.Tick += OnTimer_Ticked;
    }

    private void NetworkStatusComponent_OnLoaded(object? sender, RoutedEventArgs e)
    {
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Start();

        CheckNetworkStatus();
    }

    private void NetworkStatusComponent_OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _timer.Stop();
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
                url = "https://baidu.com";
            }

            var uri = new Uri(url);
            var host = uri.Host;

            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 5000);

            if (reply.Status == IPStatus.Success)
            {
                var delay = reply.RoundtripTime;  // 获取延迟（毫秒）
                StatusText = $"{delay}ms";

                // 根据延迟显示不同颜色
                StatusBrush = delay switch
                {
                    < 50 => new SolidColorBrush(Colors.LimeGreen),   // 优秀
                    < 100 => new SolidColorBrush(Colors.Green),      // 良好
                    < 200 => new SolidColorBrush(Colors.Orange),     // 一般
                    _ => new SolidColorBrush(Colors.Red)              // 较差
                };
            }
            else
            {
                StatusText = "超时";
                StatusBrush = new SolidColorBrush(Colors.Red);
            }
        }
        catch
        {
            StatusText = "错误";
            StatusBrush = new SolidColorBrush(Colors.Red);
        }
    }
}