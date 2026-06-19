using System.Text.Json.Serialization;

namespace SystemTools.Actions;

public class AdjustScreenBrightnessSettings
{
    [JsonPropertyName("brightnessPercent")] 
    public int BrightnessPercent { get; set; } = 50;
}