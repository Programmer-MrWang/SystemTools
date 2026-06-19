using System.Text.Json.Serialization;

namespace SystemTools.Settings;

/// <summary>
/// 切换悬浮窗主题行动的设置
/// </summary>
public class SwitchFloatingWindowThemeSettings
{
    /// <summary>
    /// 目标主题。0=跟随系统, 1=浅色, 2=深色。
    /// </summary>
    [JsonPropertyName("targetTheme")]
    public int TargetTheme { get; set; } = -1;
}
