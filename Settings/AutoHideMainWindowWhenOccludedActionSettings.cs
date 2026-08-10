using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class AutoHideMainWindowWhenOccludedActionSettings
{
    [JsonPropertyName("enable")]
    public bool Enable { get; set; } = true;

    [JsonPropertyName("notifyOnExecute")]
    public bool NotifyOnExecute { get; set; } = false;
}