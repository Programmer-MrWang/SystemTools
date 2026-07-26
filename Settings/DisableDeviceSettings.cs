using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class DisableDeviceSettings
{
    [JsonPropertyName("notifyOnExecute")]
    public bool NotifyOnExecute { get; set; } = false;

    [JsonPropertyName("deviceId")] public string DeviceId { get; set; } = string.Empty;
}