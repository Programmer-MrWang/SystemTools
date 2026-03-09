using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SystemTools.ConfigHandlers;

public class MainConfigData : INotifyPropertyChanged
{
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

        // 行动功能启用状态（Key: 行动ID, Value: 是否启用）
    [JsonPropertyName("enabledActions")] public Dictionary<string, bool> EnabledActions { get; set; } = new();

    // 触发器功能启用状态
    [JsonPropertyName("enabledTriggers")] public Dictionary<string, bool> EnabledTriggers { get; set; } = new();

    // 组件功能启用状态
    [JsonPropertyName("enabledComponents")]
    public Dictionary<string, bool> EnabledComponents { get; set; } = new();

    // 添加辅助方法检查功能是否启用
    public bool IsActionEnabled(string actionId) =>
        !EnabledActions.TryGetValue(actionId, out var enabled) || enabled;

    public bool IsTriggerEnabled(string triggerId) =>
        !EnabledTriggers.TryGetValue(triggerId, out var enabled) || enabled;

    public bool IsComponentEnabled(string componentId) =>
        !EnabledComponents.TryGetValue(componentId, out var enabled) || enabled;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
