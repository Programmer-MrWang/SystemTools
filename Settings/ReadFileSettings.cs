using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public sealed class ReadFileSettings
{
    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;
}
