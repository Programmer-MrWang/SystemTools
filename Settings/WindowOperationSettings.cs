using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class WindowOperationSettings
{
    [JsonPropertyName("operation")] public string Operation { get; set; } = "最大化";
}