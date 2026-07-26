using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class ShowFloatingWindowSettings
{
    [JsonPropertyName("notifyOnExecute")]
    public bool NotifyOnExecute { get; set; } = false;

    [JsonPropertyName("showFloatingWindow")]
    public bool ShowFloatingWindow { get; set; } = true;
}
