using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class ShortcutKeyNotificationSettings
{
    
    [JsonPropertyName("notifyOnExecute")]
    public bool NotifyOnExecute { get; set; } = false;
}