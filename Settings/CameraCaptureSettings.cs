using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class CameraCaptureSettings
{
    [JsonPropertyName("deviceName")]
    public string DeviceName { get; set; } = string.Empty;

    [JsonPropertyName("savePath")]
    public string SavePath { get; set; } = string.Empty;
}