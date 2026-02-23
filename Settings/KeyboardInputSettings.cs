using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class KeyboardInputSettings
{
    [JsonPropertyName("keys")]
    public List<string> Keys { get; set; } = [];
}