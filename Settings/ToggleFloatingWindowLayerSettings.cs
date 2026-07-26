using System.Text.Json.Serialization;

namespace SystemTools.Settings;

/// <summary>
/// 切换悬浮窗层级行动的设置
/// </summary>
public class ToggleFloatingWindowLayerSettings
{
    [JsonPropertyName("notifyOnExecute")]
    public bool NotifyOnExecute { get; set; } = false;

    /// <summary>
    /// 目标层级。-1 表示切换，0 表示置底，1 表示置顶。
    /// </summary>
    [JsonPropertyName("targetLayer")]
    public int TargetLayer { get; set; } = -1;
}
