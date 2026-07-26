// SetVolumeSettings.cs

using System.Text.Json.Serialization;

namespace SystemTools.Actions;

public class SetVolumeSettings
{
    [JsonPropertyName("notifyOnExecute")]
    public bool NotifyOnExecute { get; set; } = false;

    [JsonPropertyName("volumePercent")] public float VolumePercent { get; set; } = 50f;
}