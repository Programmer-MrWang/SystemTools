using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class ActionFlowExecutionConfirmationSettings
{
    [JsonPropertyName("promptName")]
    public string PromptName { get; set; } = "未命名自动化";
}
