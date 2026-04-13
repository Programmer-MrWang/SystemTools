using CommunityToolkit.Mvvm.ComponentModel;

namespace SystemTools.Models.ComponentSettings;

public partial class ScrollingTextSettings : ObservableObject
{
    [ObservableProperty]
    private string _textContent = "欢迎光临 SystemTools 插件！";

    [ObservableProperty]
    private double _componentWidth = 400;

    [ObservableProperty]
    private double _scrollSpeed = 50; // 像素/秒

    [ObservableProperty]
    private bool _showBorder = true;
}