using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class TypeContentSettings
{
    [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
}