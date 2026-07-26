using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class TypeContentSettings
{
    [JsonPropertyName("notifyOnExecute")]
    public bool NotifyOnExecute { get; set; } = false;

    [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
}