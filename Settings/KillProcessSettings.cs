using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class KillProcessSettings
{
    [JsonPropertyName("notifyOnExecute")]
    public bool NotifyOnExecute { get; set; } = false;

    [JsonPropertyName("processName")] public string ProcessName { get; set; } = string.Empty;
}