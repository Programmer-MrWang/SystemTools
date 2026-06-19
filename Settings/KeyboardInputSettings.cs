using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class KeyboardInputSettings
{
    [JsonPropertyName("keys")] public List<string> Keys { get; set; } = [];

    [JsonPropertyName("actions")] public List<KeyboardAction> Actions { get; set; } = [];
}

public class KeyboardAction
{
    public enum ActionType
    {
        Press,
        KeyDown,
        KeyUp
    }

    [JsonPropertyName("type")] public ActionType Type { get; set; } = ActionType.Press;

    [JsonPropertyName("keyCode")] public byte KeyCode { get; set; }

    [JsonPropertyName("keyName")] public string KeyName { get; set; } = string.Empty;

    [JsonPropertyName("interval")] public long Interval { get; set; }
}
