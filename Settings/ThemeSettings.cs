using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class ThemeSettings
{
    [JsonPropertyName("notifyOnExecute")]
    public bool NotifyOnExecute { get; set; } = false;

    [JsonPropertyName("theme")] public string Theme { get; set; } = "浅色"; // 默认浅色
}