using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class MouseInputSettings
{
    [JsonPropertyName("actions")]
    public List<MouseAction> Actions { get; set; } = new();

    [JsonPropertyName("disableMouseDuringExecution")]
    public bool DisableMouseDuringExecution { get; set; } = false;
}

public class MouseAction
{
    public enum ActionType
    {
        LeftClick,
        RightClick,
        MiddleClick,
        Scroll,
        DragMove
    }

    [JsonPropertyName("type")]
    public ActionType Type { get; set; }

    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("scrollDelta")]
    public int ScrollDelta { get; set; }

    [JsonPropertyName("interval")]
    public long Interval { get; set; }

    [JsonPropertyName("isDragEnd")]
    public bool IsDragEnd { get; set; }
}