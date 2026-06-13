using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class DoSpeechSettings
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}
