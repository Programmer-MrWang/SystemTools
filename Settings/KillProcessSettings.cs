using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class KillProcessSettings
{
    [JsonPropertyName("processName")] public string ProcessName { get; set; } = string.Empty;
}