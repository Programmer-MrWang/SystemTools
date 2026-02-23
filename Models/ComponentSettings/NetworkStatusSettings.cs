using CommunityToolkit.Mvvm.ComponentModel;

namespace SystemTools.Models.ComponentSettings;

public partial class NetworkStatusSettings : ObservableObject
{
    [ObservableProperty]
    private string _pingUrl = "https://baidu.com";

    [ObservableProperty]
    private string _displayText = "网络延迟 ";
}