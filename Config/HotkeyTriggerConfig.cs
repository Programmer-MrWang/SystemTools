using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SystemTools.Triggers;

public partial class HotkeyTriggerConfig : ObservableRecipient
{
    [ObservableProperty]
    [property: JsonIgnore]
    private string _registrationStatus = string.Empty;

    [ObservableProperty] private string _logicalKey = string.Empty;

    [JsonIgnore] public DateTime LastTriggered { get; set; } = DateTime.MinValue;

    /// <summary>
    /// 热键修饰键（Ctrl/Alt/Shift/Win）
    /// </summary>
    [ObservableProperty] private int _modifierKeys = 0; // Keys.Control | Keys.Alt 等

    /// <summary>
    /// 热键虚拟键码
    /// </summary>
    [ObservableProperty] private uint _virtualKey = 0x78; // 默认 F9

    /// <summary>
    /// 热键显示文本（如 "Ctrl+Alt+F9"）
    /// </summary>
    [ObservableProperty] private string _hotkeyDisplay = "F9";
}