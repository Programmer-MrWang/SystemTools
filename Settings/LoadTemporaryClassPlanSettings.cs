using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class LoadTemporaryClassPlanSettings
{
    [JsonPropertyName("notifyOnExecute")]
    public bool NotifyOnExecute { get; set; } = false;

    [JsonPropertyName("classPlanId")]
    public string ClassPlanId { get; set; } = string.Empty;
}
