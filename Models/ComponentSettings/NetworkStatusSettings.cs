using CommunityToolkit.Mvvm.ComponentModel;

namespace SystemTools.Models.ComponentSettings;

public enum NetworkDetectMode
{
    Auto,       // 自动
    Icmp,       // ICMP模式
    Http        // HTTP模式
}

public partial class NetworkStatusSettings : ObservableObject
{
    [ObservableProperty]
    private string _pingUrl = "https://www.baidu.com";

    [ObservableProperty]
    private string _displayText = "网络延迟 ";

    [ObservableProperty]
    private NetworkDetectMode _detectMode = NetworkDetectMode.Auto;
}