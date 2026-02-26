using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class ScreenShotSettings
{
    [JsonPropertyName("saveFolder")]
    public string SaveFolder { get; set; } = string.Empty;
}