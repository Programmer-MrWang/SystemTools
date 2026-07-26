using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class BackgroundPlayAudioSettings
{
    [JsonPropertyName("notifyOnExecute")]
    public bool NotifyOnExecute { get; set; } = false;

    [JsonPropertyName("audioFilePath")]
    public string AudioFilePath { get; set; } = string.Empty;

    [JsonPropertyName("waitForPlaybackCompleted")]
    public bool WaitForPlaybackCompleted { get; set; }
}
