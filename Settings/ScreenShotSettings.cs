using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class ScreenShotSettings
{
    [JsonPropertyName("notifyOnExecute")]
    public bool NotifyOnExecute { get; set; } = false;

    [JsonPropertyName("saveFolder")]
    public string SaveFolder { get; set; } = string.Empty;
}