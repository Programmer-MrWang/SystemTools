using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SystemTools.Models.ComponentSettings;

public enum MusicSoftware
{
    QQMusic,
    NetEaseCloudMusic,
    KugouMusic,
    //KuwoMusic,
    QishuiMusic,
    LyricifyLite
}

public partial class LyricsDisplaySettings : ObservableObject
{
    [ObservableProperty] private double _imageScale = 1.5;

    [ObservableProperty] private string _windowTitle = "桌面歌词";

    [ObservableProperty] private int _refreshRate = 100;

    [ObservableProperty] private bool _keepLyricsWindowBottom = false;

    [ObservableProperty] private MusicSoftware _selectedMusicSoftware = MusicSoftware.QQMusic;
    

    public string TargetWindowClassName => SelectedMusicSoftware switch
    {
        MusicSoftware.QQMusic => "TXGuiFoundation",
        MusicSoftware.NetEaseCloudMusic => "DesktopLyrics",
        MusicSoftware.KugouMusic => "kugou_ui",
        //MusicSoftware.KuwoMusic => "KwDeskLyricWnd",
        MusicSoftware.QishuiMusic => "Chrome_WidgetWin_1",
        MusicSoftware.LyricifyLite => "HwndWrapper",
        _ => "TXGuiFoundation"
    };

    public string? TargetWindowTitle => SelectedMusicSoftware switch
    {
        //MusicSoftware.KuwoMusic => null,
        MusicSoftware.KugouMusic => "桌面歌词 - 酷狗音乐",
        _ => "桌面歌词"
    };

    public bool CheckWindowTitle => TargetWindowTitle != null;
}