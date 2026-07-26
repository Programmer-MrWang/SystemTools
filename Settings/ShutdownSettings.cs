using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class ShutdownSettings
{
    [JsonPropertyName("notifyOnExecute")]
    public bool NotifyOnExecute { get; set; } = false;

    [JsonPropertyName("seconds")] public int Seconds { get; set; } = 60;

    [JsonPropertyName("showPrompt")] public bool ShowPrompt { get; set; } = true;
}