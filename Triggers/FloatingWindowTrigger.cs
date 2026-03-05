using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel;
using SystemTools.Services;

namespace SystemTools.Triggers;

[TriggerInfo("SystemTools.FloatingWindowTrigger", "从悬浮窗触发", "\uE7C3")]
public class FloatingWindowTrigger : TriggerBase<FloatingWindowTriggerConfig>
{
    private readonly FloatingWindowService _floatingWindowService;
    private readonly ILogger<FloatingWindowTrigger> _logger;

    public FloatingWindowTrigger(FloatingWindowService floatingWindowService, ILogger<FloatingWindowTrigger> logger)
    {
        _floatingWindowService = floatingWindowService;
        _logger = logger;
    }

    public override void Loaded()
    {
        Settings.PropertyChanged += OnSettingsChanged;
        EnsureButtonId();
        _floatingWindowService.RegisterTrigger(this);
    }

    public override void UnLoaded()
    {
        Settings.PropertyChanged -= OnSettingsChanged;
        _floatingWindowService.UnregisterTrigger(this);
    }

    public void TriggerFromFloatingWindow()
    {
        _logger.LogInformation("从悬浮窗触发触发器: {ButtonId}", Settings.ButtonId);
        Trigger();
    }

    public string GetButtonId()
    {
        EnsureButtonId();
        return Settings.ButtonId;
    }

    public string GetIcon()
    {
        return Settings.Icon;
    }

    private void EnsureButtonId()
    {
        if (string.IsNullOrWhiteSpace(Settings.ButtonId))
        {
            Settings.ButtonId = Guid.NewGuid().ToString("N");
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FloatingWindowTriggerConfig.Icon) ||
            e.PropertyName == nameof(FloatingWindowTriggerConfig.ButtonId))
        {
            _floatingWindowService.RegisterTrigger(this);
        }
    }
}
