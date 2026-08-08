using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using ClassIsland.Core.Models.Ruleset;

namespace SystemTools.ConfigHandlers;

public class MainConfigData : INotifyPropertyChanged
{
    public MainConfigData()
    {
        _aiConversationLiquidGlass.PropertyChanged += OnLiquidGlassSettingsPropertyChanged;
        _floatingWindowLiquidGlass.PropertyChanged += OnFloatingWindowLiquidGlassSettingsPropertyChanged;
    }

    public event EventHandler? RestartPropertyChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    bool _enableExperimentalFeatures;

    [JsonPropertyName("enableExperimentalFeatures")]
    public bool EnableExperimentalFeatures
    {
        get => _enableExperimentalFeatures;
        set
        {
            if (value == _enableExperimentalFeatures) return;
            _enableExperimentalFeatures = value;
            OnPropertyChanged();
            RestartPropertyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    bool _enableFfmpegFeatures;

    [JsonPropertyName("enableFfmpegFeatures")]
    public bool EnableFfmpegFeatures
    {
        get => _enableFfmpegFeatures;
        set
        {
            if (value == _enableFfmpegFeatures) return;
            _enableFfmpegFeatures = value;
            OnPropertyChanged();
            RestartPropertyChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    

    bool _enableFloatingWindowFeature = true;

    [JsonPropertyName("enableFloatingWindowFeature")]
    public bool EnableFloatingWindowFeature
    {
        get => _enableFloatingWindowFeature;
        set
        {
            if (value == _enableFloatingWindowFeature) return;
            _enableFloatingWindowFeature = value;
            OnPropertyChanged();
            RestartPropertyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

        bool _lyricifyLiteWarningDismissed;

    [JsonPropertyName("lyricifyLiteWarningDismissed")]
    public bool LyricifyLiteWarningDismissed
    {
        get => _lyricifyLiteWarningDismissed;
        set
        {
            if (value == _lyricifyLiteWarningDismissed) return;
            _lyricifyLiteWarningDismissed = value;
            OnPropertyChanged();
        }
    }
    
    bool _enableFaceRecognition;

    [JsonPropertyName("enableFaceRecognition")]
    public bool EnableFaceRecognition
    {
        get => _enableFaceRecognition;
        set
        {
            if (value == _enableFaceRecognition) return;
            _enableFaceRecognition = value;
            OnPropertyChanged();
            RestartPropertyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    bool _autoSwitchClassIslandTheme;

    [JsonPropertyName("autoSwitchClassIslandTheme")]
    public bool AutoSwitchClassIslandTheme
    {
        get => _autoSwitchClassIslandTheme;
        set
        {
            if (value == _autoSwitchClassIslandTheme) return;
            _autoSwitchClassIslandTheme = value;
            OnPropertyChanged();
        }
    }

    bool _autoOpenUsbDriveOnInsert;

    [JsonPropertyName("autoOpenUsbDriveOnInsert")]
    public bool AutoOpenUsbDriveOnInsert
    {
        get => _autoOpenUsbDriveOnInsert;
        set
        {
            if (value == _autoOpenUsbDriveOnInsert) return;
            _autoOpenUsbDriveOnInsert = value;
            OnPropertyChanged();
        }
    }



    bool _autoCleanupClassIslandMemory;

    [JsonPropertyName("autoCleanupClassIslandMemory")]
    public bool AutoCleanupClassIslandMemory
    {
        get => _autoCleanupClassIslandMemory;
        set
        {
            if (value == _autoCleanupClassIslandMemory) return;
            _autoCleanupClassIslandMemory = value;
            OnPropertyChanged();
        }
    }

    bool _autoCleanupSystemMemory;

    [JsonPropertyName("autoCleanupSystemMemory")]
    public bool AutoCleanupSystemMemory
    {
        get => _autoCleanupSystemMemory;
        set
        {
            if (value == _autoCleanupSystemMemory) return;
            _autoCleanupSystemMemory = value;
            OnPropertyChanged();
        }
    }

    int _systemMemoryCleanupThresholdPercent = 90;

    [JsonPropertyName("systemMemoryCleanupThresholdPercent")]
    public int SystemMemoryCleanupThresholdPercent
    {
        get => _systemMemoryCleanupThresholdPercent;
        set
        {
            var clamped = Math.Clamp(value, 50, 99);
            if (clamped == _systemMemoryCleanupThresholdPercent) return;
            _systemMemoryCleanupThresholdPercent = clamped;
            OnPropertyChanged();
        }
    }

    bool _autoHideMainWindowWhenOccluded;

    [JsonPropertyName("autoHideMainWindowWhenOccluded")]
    public bool AutoHideMainWindowWhenOccluded
    {
        get => _autoHideMainWindowWhenOccluded;
        set
        {
            if (value == _autoHideMainWindowWhenOccluded) return;
            _autoHideMainWindowWhenOccluded = value;
            OnPropertyChanged();
        }
    }
    
    bool _enableAiService;

    [JsonPropertyName("enableAiService")]
    public bool EnableAiService
    {
        get => _enableAiService;
        set
        {
            if (value == _enableAiService) return;
            _enableAiService = value;
            OnPropertyChanged();
            RestartPropertyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    int _aiConversationFloatingWindowStyle;

    [JsonPropertyName("aiConversationFloatingWindowStyle")]
    public int AiConversationFloatingWindowStyle
    {
        get => _aiConversationFloatingWindowStyle;
        set
        {
            var normalized = value == 1 ? 1 : 0;
            if (normalized == _aiConversationFloatingWindowStyle) return;
            _aiConversationFloatingWindowStyle = normalized;
            OnPropertyChanged();
        }
    }

    LiquidGlassSettings _aiConversationLiquidGlass = new();

    [JsonPropertyName("aiConversationLiquidGlass")]
    public LiquidGlassSettings AiConversationLiquidGlass
    {
        get => _aiConversationLiquidGlass;
        set
        {
            value ??= new LiquidGlassSettings();
            if (ReferenceEquals(value, _aiConversationLiquidGlass)) return;
            _aiConversationLiquidGlass.PropertyChanged -= OnLiquidGlassSettingsPropertyChanged;
            _aiConversationLiquidGlass = value;
            _aiConversationLiquidGlass.PropertyChanged += OnLiquidGlassSettingsPropertyChanged;
            OnPropertyChanged();
        }
    }

    string _aiProviderName = "OpenAI";

    [JsonPropertyName("aiProviderName")]
    public string AiProviderName
    {
        get => _aiProviderName;
        set
        {
            value ??= string.Empty;
            if (string.Equals(value, _aiProviderName, StringComparison.Ordinal)) return;
            _aiProviderName = value;
            OnPropertyChanged();
        }
    }

    string _aiApiKey = string.Empty;

    [JsonPropertyName("aiApiKey")]
    public string AiApiKey
    {
        get => _aiApiKey;
        set
        {
            value ??= string.Empty;
            if (string.Equals(value, _aiApiKey, StringComparison.Ordinal)) return;
            _aiApiKey = value;
            OnPropertyChanged();
        }
    }

    string _aiApiUrl = "https://api.openai.com/v1";

    [JsonPropertyName("aiApiUrl")]
    public string AiApiUrl
    {
        get => _aiApiUrl;
        set
        {
            value ??= string.Empty;
            if (string.Equals(value, _aiApiUrl, StringComparison.Ordinal)) return;
            _aiApiUrl = value;
            OnPropertyChanged();
        }
    }

    string _aiModel = string.Empty;

    [JsonPropertyName("aiModel")]
    public string AiModel
    {
        get => _aiModel;
        set
        {
            value ??= string.Empty;
            if (string.Equals(value, _aiModel, StringComparison.Ordinal)) return;
            _aiModel = value;
            OnPropertyChanged();
        }
    }

    bool _shareAiRepliesWithClassIslandNotifications;

    [JsonPropertyName("shareAiRepliesWithClassIslandNotifications")]
    public bool ShareAiRepliesWithClassIslandNotifications
    {
        get => _shareAiRepliesWithClassIslandNotifications;
        set
        {
            if (value == _shareAiRepliesWithClassIslandNotifications) return;
            _shareAiRepliesWithClassIslandNotifications = value;
            OnPropertyChanged();
        }
    }

    bool _enableVoiceWakeAi;

    [JsonPropertyName("enableVoiceWakeAi")]
    public bool EnableVoiceWakeAi
    {
        get => _enableVoiceWakeAi;
        set
        {
            if (value == _enableVoiceWakeAi) return;
            _enableVoiceWakeAi = value;
            OnPropertyChanged();
        }
    }

    string _aiWakeWord = "你好ci";

    [JsonPropertyName("aiWakeWord")]
    public string AiWakeWord
    {
        get => _aiWakeWord;
        set
        {
            value = string.IsNullOrWhiteSpace(value) ? "你好ci" : value.Trim();
            if (string.Equals(value, _aiWakeWord, StringComparison.Ordinal)) return;
            _aiWakeWord = value;
            OnPropertyChanged();
        }
    }

    // ========== 公告相关 ==========
    /*string _lastAcceptedAnnouncement = string.Empty;

    [JsonPropertyName("lastAcceptedAnnouncement")]
    public string LastAcceptedAnnouncement
    {
        get => _lastAcceptedAnnouncement;
        set
        {
            if (value == _lastAcceptedAnnouncement) return;
            _lastAcceptedAnnouncement = value;
            OnPropertyChanged();
        }
    }*/



    bool _showFloatingWindow = true;

    [JsonPropertyName("showFloatingWindow")]
    public bool ShowFloatingWindow
    {
        get => _showFloatingWindow;
        set
        {
            if (value == _showFloatingWindow) return;
            _showFloatingWindow = value;
            OnPropertyChanged();
        }
    }

    bool _floatingWindowHorizontal;

    [JsonPropertyName("floatingWindowHorizontal")]
    public bool FloatingWindowHorizontal
    {
        get => _floatingWindowHorizontal;
        set
        {
            if (value == _floatingWindowHorizontal) return;
            _floatingWindowHorizontal = value;
            OnPropertyChanged();
        }
    }

    [JsonPropertyName("floatingWindowButtonOrder")]
    public List<string> FloatingWindowButtonOrder { get; set; } = new();

    [JsonPropertyName("floatingWindowButtonRows")]
    public List<List<string>> FloatingWindowButtonRows { get; set; } = new();


    double _floatingWindowScale = 1.0;

    [JsonPropertyName("floatingWindowScale")]
    public double FloatingWindowScale
    {
        get => _floatingWindowScale;
        set
        {
            var clamped = Math.Clamp(value, 0.5, 2.0);
            if (Math.Abs(clamped - _floatingWindowScale) < 0.0001) return;
            _floatingWindowScale = clamped;
            OnPropertyChanged();
        }
    }

    int _floatingWindowTextSize = 12;

    [JsonPropertyName("floatingWindowTextSize")]
    public int FloatingWindowTextSize
    {
        get => _floatingWindowTextSize;
        set
        {
            var clamped = Math.Clamp(value, 8, 30);
            if (clamped == _floatingWindowTextSize) return;
            _floatingWindowTextSize = clamped;
            OnPropertyChanged();
        }
    }

    int _floatingWindowIconSize = 22;

    [JsonPropertyName("floatingWindowIconSize")]
    public int FloatingWindowIconSize
    {
        get => _floatingWindowIconSize;
        set
        {
            var clamped = Math.Clamp(value, 15, 50);
            if (clamped == _floatingWindowIconSize) return;
            _floatingWindowIconSize = clamped;
            OnPropertyChanged();
        }
    }

    int _floatingWindowOpacity = 80;

    [JsonPropertyName("floatingWindowOpacity")]
    public int FloatingWindowOpacity
    {
        get => _floatingWindowOpacity;
        set
        {
            var clamped = Math.Clamp(value, 10, 100);
            if (clamped == _floatingWindowOpacity) return;
            _floatingWindowOpacity = clamped;
            OnPropertyChanged();
        }
    }


    bool _floatingWindowShadowEnabled = true;

    [JsonPropertyName("floatingWindowShadowEnabled")]
    public bool FloatingWindowShadowEnabled
    {
        get => _floatingWindowShadowEnabled;
        set
        {
            if (value == _floatingWindowShadowEnabled) return;
            _floatingWindowShadowEnabled = value;
            OnPropertyChanged();
        }
    }

    int _floatingWindowTheme = 0;

    [JsonPropertyName("floatingWindowTheme")]
    public int FloatingWindowTheme
    {
        get => _floatingWindowTheme;
        set
        {
            var normalized = value is 1 or 2 or 3 ? value : 0;
            if (normalized == _floatingWindowTheme) return;
            _floatingWindowTheme = normalized;
            OnPropertyChanged();
        }
    }

    int _floatingWindowPositionX = 100;

    [JsonPropertyName("floatingWindowPositionX")]
    public int FloatingWindowPositionX
    {
        get => _floatingWindowPositionX;
        set
        {
            if (value == _floatingWindowPositionX) return;
            _floatingWindowPositionX = value;
            OnPropertyChanged();
        }
    }

    int _floatingWindowPositionY = 100;

    [JsonPropertyName("floatingWindowPositionY")]
    public int FloatingWindowPositionY
    {
        get => _floatingWindowPositionY;
        set
        {
            if (value == _floatingWindowPositionY) return;
            _floatingWindowPositionY = value;
            OnPropertyChanged();
        }
    }

    [JsonPropertyName("actionFlowExecutionConfirmationPositionX")]
    public int? ActionFlowExecutionConfirmationPositionX { get; set; }

    [JsonPropertyName("actionFlowExecutionConfirmationPositionY")]
    public int? ActionFlowExecutionConfirmationPositionY { get; set; }

    [JsonPropertyName("actionFlowExecutionDelayPositionX")]
    public int? ActionFlowExecutionDelayPositionX { get; set; }

    [JsonPropertyName("actionFlowExecutionDelayPositionY")]
    public int? ActionFlowExecutionDelayPositionY { get; set; }

    int _floatingWindowLayer = 1;

    [JsonPropertyName("floatingWindowLayer")]
    public int FloatingWindowLayer
    {
        get => _floatingWindowLayer;
        set
        {
            var normalized = value is 0 or 1 ? value : 1;
            if (normalized == _floatingWindowLayer) return;
            _floatingWindowLayer = normalized;
            OnPropertyChanged();
        }
    }

    int _floatingWindowLayerRecheckMode = 1;

    [JsonPropertyName("floatingWindowLayerRecheckMode")]
    public int FloatingWindowLayerRecheckMode
    {
        get => _floatingWindowLayerRecheckMode;
        set
        {
            var normalized = Math.Clamp(value, 0, 3);
            if (normalized == _floatingWindowLayerRecheckMode) return;
            _floatingWindowLayerRecheckMode = normalized;
            OnPropertyChanged();
        }
    }

    string _currentFloatingWindowProfile = "Default";

    [JsonPropertyName("currentFloatingWindowProfile")]
    public string CurrentFloatingWindowProfile
    {
        get => _currentFloatingWindowProfile;
        set
        {
            if (string.Equals(value, _currentFloatingWindowProfile, StringComparison.Ordinal)) return;
            _currentFloatingWindowProfile = value;
            OnPropertyChanged();
        }
    }

    bool _floatingWindowRulesetEnabled = false;

    [JsonPropertyName("floatingWindowRulesetEnabled")]
    public bool FloatingWindowRulesetEnabled
    {
        get => _floatingWindowRulesetEnabled;
        set
        {
            if (value == _floatingWindowRulesetEnabled) return;
            _floatingWindowRulesetEnabled = value;
            OnPropertyChanged();
        }
    }

    bool _floatingWindowDragHandleAlwaysVisible = false;

    [JsonPropertyName("floatingWindowDragHandleAlwaysVisible")]
    public bool FloatingWindowDragHandleAlwaysVisible
    {
        get => _floatingWindowDragHandleAlwaysVisible;
        set
        {
            if (value == _floatingWindowDragHandleAlwaysVisible) return;
            _floatingWindowDragHandleAlwaysVisible = value;
            OnPropertyChanged();
        }
    }

    [JsonPropertyName("floatingWindowRuleset")]
    public Ruleset FloatingWindowRuleset { get; set; } = new();

    [JsonPropertyName("floatingWindowButtonRulesets")]
    public Dictionary<string, ButtonRulesetConfig> FloatingWindowButtonRulesets { get; set; } = new();

    [JsonPropertyName("floatingWindowRowRulesets")]
    public List<RowRulesetConfig> FloatingWindowRowRulesets { get; set; } = new();

        // 行动功能启用状态（Key: 行动ID, Value: 是否启用）
    [JsonPropertyName("enabledActions")] public Dictionary<string, bool> EnabledActions { get; set; } = new();

    // 触发器功能启用状态
    [JsonPropertyName("enabledTriggers")] public Dictionary<string, bool> EnabledTriggers { get; set; } = new();

    // 组件功能启用状态
    [JsonPropertyName("enabledComponents")]
    public Dictionary<string, bool> EnabledComponents { get; set; } = new();

    // 规则功能启用状态
    [JsonPropertyName("enabledRules")]
    public Dictionary<string, bool> EnabledRules { get; set; } = new();

    // 添加辅助方法检查功能是否启用
    public bool IsActionEnabled(string actionId) =>
        !EnabledActions.TryGetValue(actionId, out var enabled) || enabled;

    public bool IsTriggerEnabled(string triggerId) =>
        !EnabledTriggers.TryGetValue(triggerId, out var enabled) || enabled;

    public bool IsComponentEnabled(string componentId) =>
        !EnabledComponents.TryGetValue(componentId, out var enabled) || enabled;

    public bool IsRuleEnabled(string ruleId) =>
        !EnabledRules.TryGetValue(ruleId, out var enabled) || enabled;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private int _floatingWindowAppearanceStyle = 1;

    [JsonPropertyName("floatingWindowAppearanceStyle")]
    public int FloatingWindowAppearanceStyle
    {
        get => _floatingWindowAppearanceStyle;
        set
        {
            var normalized = value == 1 ? 1 : 0;
            if (normalized == _floatingWindowAppearanceStyle) return;
            _floatingWindowAppearanceStyle = normalized;
            OnPropertyChanged();
        }
    }

    private LiquidGlassSettings _floatingWindowLiquidGlass = CreateFloatingWindowLiquidGlassDefaults();

    [JsonPropertyName("floatingWindowLiquidGlass")]
    public LiquidGlassSettings FloatingWindowLiquidGlass
    {
        get => _floatingWindowLiquidGlass;
        set
        {
            value ??= CreateFloatingWindowLiquidGlassDefaults();
            if (ReferenceEquals(value, _floatingWindowLiquidGlass)) return;
            _floatingWindowLiquidGlass.PropertyChanged -= OnFloatingWindowLiquidGlassSettingsPropertyChanged;
            _floatingWindowLiquidGlass = value;
            _floatingWindowLiquidGlass.PropertyChanged += OnFloatingWindowLiquidGlassSettingsPropertyChanged;
            OnPropertyChanged();
        }
    }

    private double _floatingWindowGlassButtonScaleDip = 3.5;

    [JsonPropertyName("floatingWindowGlassButtonScaleDip")]
    public double FloatingWindowGlassButtonScaleDip
    {
        get => _floatingWindowGlassButtonScaleDip;
        set
        {
            var clamped = Math.Clamp(value, 0, 12);
            if (Math.Abs(clamped - _floatingWindowGlassButtonScaleDip) < 0.0001) return;
            _floatingWindowGlassButtonScaleDip = clamped;
            OnPropertyChanged();
        }
    }

    private void OnLiquidGlassSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        OnPropertyChanged(nameof(AiConversationLiquidGlass));

    private void OnFloatingWindowLiquidGlassSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        OnPropertyChanged(nameof(FloatingWindowLiquidGlass));

    private static LiquidGlassSettings CreateFloatingWindowLiquidGlassDefaults()
    {
        return new LiquidGlassSettings
        {
            CornerRadius = 20,
            BackdropRefreshIntervalMs = 50,
            RefractionHeight = 10,
            RefractionAmount = 20,
            BlurRadius = 4,
            Vibrancy = 1.25,
            BackdropOpacity = 0.96,
            HighlightEnabled = true,
            HighlightWidth = 0.5,
            HighlightBlurRadius = 0.3,
            HighlightOpacity = 0.65,
            ShadowEnabled = true,
            ShadowRadius = 20,
            ShadowOffsetY = 4,
            ShadowColor = "#40000000",
            ShadowOpacity = 0.85
        };
    }
}
