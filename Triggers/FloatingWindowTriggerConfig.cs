using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SystemTools.Triggers;

/// <summary>
/// 悬浮窗触发器的配置
/// </summary>
public partial class FloatingWindowTriggerConfig : ObservableObject
{
    [ObservableProperty]
    [JsonPropertyName("buttonId")]
    private string _buttonId = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    [JsonPropertyName("icon")]
    private string _icon = "/uEA37";

    [ObservableProperty]
    [JsonPropertyName("buttonName")]
    private string _buttonName = "触发按钮 1";

    [ObservableProperty]
    [JsonPropertyName("isVisible")]
    private bool _isVisible = true;

    [ObservableProperty]
    [JsonPropertyName("position")]
    private int _position = -1;
}
