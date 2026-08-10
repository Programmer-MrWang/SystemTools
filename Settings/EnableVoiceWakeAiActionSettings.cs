using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class EnableVoiceWakeAiActionSettings
{
    [JsonPropertyName("enable")]
    public bool Enable { get; set; } = true;

    [JsonPropertyName("notifyOnExecute")]
    public bool NotifyOnExecute { get; set; } = false;
}