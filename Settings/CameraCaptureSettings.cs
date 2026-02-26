using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class CameraCaptureSettings
{
    [JsonPropertyName("deviceName")] 
    public string DeviceName { get; set; } = string.Empty;

    [JsonPropertyName("saveFolder")]
    public string SaveFolder { get; set; } = string.Empty;
}