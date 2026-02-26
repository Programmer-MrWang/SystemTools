using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class EnableDeviceSettings
{
    [JsonPropertyName("deviceId")] public string DeviceId { get; set; } = string.Empty;
}