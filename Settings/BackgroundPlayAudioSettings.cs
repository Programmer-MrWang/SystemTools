using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class BackgroundPlayAudioSettings
{
    [JsonPropertyName("audioFilePath")]
    public string AudioFilePath { get; set; } = string.Empty;

    [JsonPropertyName("waitForPlaybackCompleted")]
    public bool WaitForPlaybackCompleted { get; set; }
}
