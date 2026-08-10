using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class AutoSwitchClassIslandThemeActionSettings
{
    [JsonPropertyName("enable")]
    public bool Enable { get; set; } = true;

    [JsonPropertyName("notifyOnExecute")]
    public bool NotifyOnExecute { get; set; } = false;
}