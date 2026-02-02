using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class ScreenShotSettings
{
    [JsonPropertyName("savePath")]
    public string SavePath { get; set; } = string.Empty;
}