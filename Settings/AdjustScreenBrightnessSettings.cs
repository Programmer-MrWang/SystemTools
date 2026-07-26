using System.Text.Json.Serialization;

namespace SystemTools.Actions;

public class AdjustScreenBrightnessSettings
{
    [JsonPropertyName("notifyOnExecute")]
    public bool NotifyOnExecute { get; set; } = false;

    [JsonPropertyName("brightnessPercent")] 
    public int BrightnessPercent { get; set; } = 50;
}