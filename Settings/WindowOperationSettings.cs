using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class WindowOperationSettings
{
    [JsonPropertyName("notifyOnExecute")]
    public bool NotifyOnExecute { get; set; } = false;

    [JsonPropertyName("operation")] public string Operation { get; set; } = "最大化";
}