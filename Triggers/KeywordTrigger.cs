using System;
using System.ComponentModel;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using SystemTools.Services;

namespace SystemTools.Triggers;

[TriggerInfo("SystemTools.KeywordTrigger", "关键词触发", "\uED53")]
public class KeywordTrigger : TriggerBase<KeywordTriggerConfig>
{
    private readonly ILogger<KeywordTrigger> _logger;
    private readonly KeywordSpeechService _speechService;
    private IDisposable? _registration;

    public KeywordTrigger(ILogger<KeywordTrigger> logger, KeywordSpeechService speechService)
    {
        _logger = logger;
        _speechService = speechService;
    }

    public override void Loaded()
    {
        Settings.PropertyChanged += OnSettingsPropertyChanged;
        RegisterWithSpeechService();
    }

    public override void UnLoaded()
    {
        Settings.PropertyChanged -= OnSettingsPropertyChanged;
        UnregisterFromSpeechService();
    }

    private void RegisterWithSpeechService()
    {
        UnregisterFromSpeechService();
        var keyword = Settings.Keyword;
        if (string.IsNullOrWhiteSpace(keyword)) return;
        _registration = _speechService.Register(keyword, Settings.Threshold, OnKeywordMatched);
        _logger.LogInformation("关键词触发器已注册: \"{Keyword}\" (阈值: {Threshold:F2})", keyword, Settings.Threshold);
    }

    private void UnregisterFromSpeechService()
    {
        _registration?.Dispose();
        _registration = null;
    }

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(KeywordTriggerConfig.Keyword) or nameof(KeywordTriggerConfig.Threshold))
            RegisterWithSpeechService();
    }

    private void OnKeywordMatched()
    {
        if (DateTime.Now - Settings.LastTriggered < TimeSpan.FromMilliseconds(500)) return;
        Settings.LastTriggered = DateTime.Now;
        _logger.LogInformation("关键词 \"{Keyword}\" 被识别，触发自动化", Settings.Keyword);
        Trigger();
    }
}
