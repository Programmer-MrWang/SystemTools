using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared.Enums;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel;
using System.Linq;
using SystemTools.Services;

namespace SystemTools.Triggers;

[TriggerInfo("SystemTools.FloatingWindowTrigger", "从悬浮窗触发", "\uEA37")]
public class FloatingWindowTrigger : TriggerBase<FloatingWindowTriggerConfig>
{
    private readonly FloatingWindowService _floatingWindowService;
    private readonly ILogger<FloatingWindowTrigger> _logger;
    private bool _isNormalizingButtonId;

    public FloatingWindowTrigger(FloatingWindowService floatingWindowService, ILogger<FloatingWindowTrigger> logger)
    {
        _floatingWindowService = floatingWindowService;
        _logger = logger;
    }

    public override void Loaded()
    {
        Settings.PropertyChanged += OnSettingsChanged;
        if (AssociatedWorkflow?.ActionSet != null)
        {
            AssociatedWorkflow.ActionSet.PropertyChanged += OnActionSetPropertyChanged;
        }
        EnsureButtonId();
        _floatingWindowService.RegisterTrigger(this);
    }

    public override void UnLoaded()
    {
        Settings.PropertyChanged -= OnSettingsChanged;
        if (AssociatedWorkflow?.ActionSet != null)
        {
            AssociatedWorkflow.ActionSet.PropertyChanged -= OnActionSetPropertyChanged;
        }
        _floatingWindowService.UnregisterTrigger(this);
    }

    public void TriggerFromFloatingWindow()
    {
        var actionSet = AssociatedWorkflow?.ActionSet;
        var useRevertMode = actionSet?.IsRevertEnabled == true;
        var isOn = actionSet?.Status == ActionSetStatus.IsOn;

        _logger.LogInformation("从悬浮窗触发触发器: {ButtonId}, 启用恢复: {UseRevertMode}, 当前状态: {Status}",
            Settings.ButtonId, useRevertMode, actionSet?.Status);

        if (useRevertMode && isOn)
        {
            TriggerRevert();
            return;
        }

        Trigger();
    }

    public string GetButtonId()
    {
        EnsureButtonId();
        return Settings.ButtonId;
    }

    public string GetUniqueButtonId(Func<string, bool> isButtonIdInUse)
    {
        EnsureButtonId();
        if (!isButtonIdInUse(Settings.ButtonId))
        {
            return Settings.ButtonId;
        }

        _isNormalizingButtonId = true;
        try
        {
            do
            {
                Settings.ButtonId = Guid.NewGuid().ToString("N");
            } while (isButtonIdInUse(Settings.ButtonId));
        }
        finally
        {
            _isNormalizingButtonId = false;
        }

        _logger.LogInformation("检测到重复的悬浮窗按钮 ID，已为触发器生成新 ID: {ButtonId}", Settings.ButtonId);
        return Settings.ButtonId;
    }

    public string GetIcon()
    {
        return Settings.Icon;
    }

    public string GetButtonName()
    {
        return ShouldShowRevertButton() ? "恢复" : Settings.ButtonName;
    }

    public string GetLayoutButtonName()
    {
        return Settings.ButtonName;
    }

    public bool ShouldUseRevertStyle()
    {
        return ShouldShowRevertButton();
    }

    public bool IsRevertEnabled()
    {
        return AssociatedWorkflow?.ActionSet?.IsRevertEnabled == true;
    }

    public void CancelIsOnState()
    {
        var actionSet = AssociatedWorkflow?.ActionSet;
        if (actionSet == null || actionSet.Status != ActionSetStatus.IsOn)
        {
            return;
        }

        var statusProperty = actionSet.GetType().GetProperty("Status");
        if (statusProperty?.CanWrite != true || !statusProperty.PropertyType.IsEnum)
        {
            return;
        }

        var enumValues = Enum.GetValues(statusProperty.PropertyType).Cast<object>().ToList();
        var targetStatus = enumValues.FirstOrDefault(v =>
                               string.Equals(v.ToString(), "IsOff", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(v.ToString(), "Off", StringComparison.OrdinalIgnoreCase))
                           ?? enumValues.FirstOrDefault(v => !Equals(v, actionSet.Status));

        if (targetStatus == null)
        {
            return;
        }

        statusProperty.SetValue(actionSet, targetStatus);
        _logger.LogInformation("通过悬浮窗右键取消等待恢复状态: {ButtonId}, 原状态: {OldStatus}, 新状态: {NewStatus}",
            Settings.ButtonId, ActionSetStatus.IsOn, targetStatus);
        _floatingWindowService.RegisterTrigger(this);
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
        if (_isNormalizingButtonId)
        {
            return;
        }

        if (e.PropertyName == nameof(FloatingWindowTriggerConfig.Icon) ||
            e.PropertyName == nameof(FloatingWindowTriggerConfig.ButtonId) ||
            e.PropertyName == nameof(FloatingWindowTriggerConfig.ButtonName))
        {
            _floatingWindowService.RegisterTrigger(this);
        }
    }

    private void OnActionSetPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "Status" ||
            e.PropertyName == "IsRevertEnabled")
        {
            _floatingWindowService.RegisterTrigger(this);
        }
    }

    private bool ShouldShowRevertButton()
    {
        var actionSet = AssociatedWorkflow?.ActionSet;
        return actionSet?.IsRevertEnabled == true && actionSet.Status == ActionSetStatus.IsOn;
    }
}
