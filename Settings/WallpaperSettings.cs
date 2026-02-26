using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class ChangeWallpaperSettings
{
    [JsonPropertyName("imagePath")] public string ImagePath { get; set; } = string.Empty;
}