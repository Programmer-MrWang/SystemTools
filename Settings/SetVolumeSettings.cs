// SetVolumeSettings.cs

using System.Text.Json.Serialization;

namespace SystemTools.Actions;

public class SetVolumeSettings
{
    [JsonPropertyName("volumePercent")] public float VolumePercent { get; set; } = 50f;
}