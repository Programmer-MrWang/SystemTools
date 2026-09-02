using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class ToggleFloatingWindowLayerSettings
{
    [JsonPropertyName("notifyOnExecute")]
    public bool NotifyOnExecute { get; set; } = false;

    // -1 表示切换，0 表示置底，1 表示置顶。
    [JsonPropertyName("targetLayer")]
    public int TargetLayer { get; set; } = -1;
}
