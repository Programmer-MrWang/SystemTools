using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SystemTools.ConfigHandlers;

/// <summary>
/// 悬浮窗配置方案，保存一套完整的悬浮窗布局和外观配置。
/// 注意：显示状态(ShowFloatingWindow)和规则集(HideOnRule/HidingRules)是全局设置，不随方案切换。
/// </summary>
public partial class FloatingWindowProfile : ObservableObject
{
    [ObservableProperty]
    [JsonPropertyName("name")]
    private string _name = "Default";

    [ObservableProperty]
    [JsonPropertyName("floatingWindowHorizontal")]
    private bool _floatingWindowHorizontal;

    [JsonPropertyName("floatingWindowButtonOrder")]
    public List<string> FloatingWindowButtonOrder { get; set; } = new();

    [JsonPropertyName("floatingWindowButtonRows")]
    public List<List<string>> FloatingWindowButtonRows { get; set; } = new();

    [ObservableProperty]
    [JsonPropertyName("floatingWindowScale")]
    private double _floatingWindowScale = 1.0;

    [ObservableProperty]
    [JsonPropertyName("floatingWindowIconSize")]
    private int _floatingWindowIconSize = 22;

    [ObservableProperty]
    [JsonPropertyName("floatingWindowTextSize")]
    private int _floatingWindowTextSize = 12;

    [ObservableProperty]
    [JsonPropertyName("floatingWindowOpacity")]
    private int _floatingWindowOpacity = 80;

    [ObservableProperty]
    [JsonPropertyName("floatingWindowPositionX")]
    private int _floatingWindowPositionX = 100;

    [ObservableProperty]
    [JsonPropertyName("floatingWindowPositionY")]
    private int _floatingWindowPositionY = 100;

    [ObservableProperty]
    [JsonPropertyName("floatingWindowLayer")]
    private int _floatingWindowLayer = 1;

    [ObservableProperty]
    [JsonPropertyName("floatingWindowLayerRecheckMode")]
    private int _floatingWindowLayerRecheckMode = 1;

    [ObservableProperty]
    [JsonPropertyName("floatingWindowShadowEnabled")]
    private bool _floatingWindowShadowEnabled = true;

    [ObservableProperty]
    [JsonPropertyName("floatingWindowDragHandleAlwaysVisible")]
    private bool _floatingWindowDragHandleAlwaysVisible;

    [JsonPropertyName("floatingWindowButtonRulesets")]
    public Dictionary<string, ButtonRulesetConfig> FloatingWindowButtonRulesets { get; set; } = new();

    [JsonPropertyName("floatingWindowRowRulesets")]
    public List<RowRulesetConfig> FloatingWindowRowRulesets { get; set; } = new();

    /// <summary>
    /// 清理不存在的按钮ID，返回是否有变更
    /// </summary>
    public bool PruneInvalidButtonIds(IEnumerable<string> validButtonIds)
    {
        var validSet = validButtonIds.ToHashSet();
        var changed = false;

        var newOrder = FloatingWindowButtonOrder.Where(id => validSet.Contains(id)).ToList();
        if (newOrder.Count != FloatingWindowButtonOrder.Count)
        {
            FloatingWindowButtonOrder = newOrder;
            changed = true;
        }

        var newRows = FloatingWindowButtonRows
            .Select(row => row.Where(id => validSet.Contains(id)).ToList())
            .ToList();
        if (newRows.Count != FloatingWindowButtonRows.Count ||
            newRows.Zip(FloatingWindowButtonRows, (a, b) => a.SequenceEqual(b)).Any(x => !x))
        {
            FloatingWindowButtonRows = newRows;
            changed = true;
        }

        var invalidButtonConfigs = FloatingWindowButtonRulesets.Keys.Where(id => !validSet.Contains(id)).ToList();
        foreach (var id in invalidButtonConfigs)
        {
            FloatingWindowButtonRulesets.Remove(id);
            changed = true;
        }

        return changed;
    }
}
