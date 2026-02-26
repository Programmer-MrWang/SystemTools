using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class TriggerCustomTriggerSettings
{
    [JsonPropertyName("triggerId")] public string TriggerId { get; set; } = string.Empty;
}