using System.Text.Json.Serialization;
using ClassIsland.Core.Models.Ruleset;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SystemTools.ConfigHandlers;

/// <summary>
/// 悬浮窗按钮的规则集配置
/// </summary>
public partial class ButtonRulesetConfig : ObservableObject
{
    [ObservableProperty]
    [JsonPropertyName("isVisible")]
    private bool _isVisible = true;

    [ObservableProperty]
    [JsonPropertyName("hideOnRule")]
    private bool _hideOnRule;

    [ObservableProperty]
    [JsonPropertyName("hidingRules")]
    private Ruleset _hidingRules = new();
}
