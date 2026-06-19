using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class AccentColorSettings
{
    [JsonPropertyName("colorHex")] public string ColorHex { get; set; } = "#FF0078D4";
}
