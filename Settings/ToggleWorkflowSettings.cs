using System.Text.Json.Serialization;
using SystemTools.Converters;

namespace SystemTools.Settings;

/// <summary>
/// 切换自动化启用状态的设置
/// </summary>
public class ToggleWorkflowSettings
{
    /// <summary>
    /// 目标自动化的名称（用于显示）
    /// </summary>
    [JsonPropertyName("targetWorkflowName")]
    public string TargetWorkflowName { get; set; } = string.Empty;

    /// <summary>
    /// 目标自动化在列表中的索引
    /// </summary>
    [JsonPropertyName("targetWorkflowIndex")]
    public int TargetWorkflowIndex { get; set; } = -1;

    /// <summary>
    /// 操作模式：true=启用, false=禁用, null=切换
    /// </summary>
    [JsonPropertyName("enableMode")]
    [JsonConverter(typeof(NullableBooleanStringConverter))]
    public bool? EnableMode { get; set; } = null;

    /// <summary>
    /// 是否在触发器恢复时自动恢复原状态
    /// </summary>
    [JsonPropertyName("revertToOriginal")]
    public bool RevertToOriginal { get; set; } = true;
}
