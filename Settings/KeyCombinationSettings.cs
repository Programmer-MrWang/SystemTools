using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class KeyCombinationSettings
{
    [JsonPropertyName("notifyOnExecute")]
    public bool NotifyOnExecute { get; set; } = false;

    [JsonPropertyName("keys")]
    public List<KeyCombinationKey> Keys { get; set; } = [];
}

public class KeyCombinationKey
{
    [JsonPropertyName("keyCode")]
    public byte? KeyCode { get; set; }

    [JsonPropertyName("keyName")]
    public string KeyName { get; set; } = string.Empty;
}