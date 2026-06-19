using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using SystemTools.Services;
using SystemTools.Settings;
using SystemTools.Shared;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.ShowFloatingWindow", "显示悬浮窗", "\uEA37", false)]
public class ShowFloatingWindowAction(
    ILogger<ShowFloatingWindowAction> logger,
    FloatingWindowService floatingWindowService) : ActionBase<ShowFloatingWindowSettings>
{
    private readonly ILogger<ShowFloatingWindowAction> _logger = logger;
    private readonly FloatingWindowService _floatingWindowService = floatingWindowService;
    private static readonly ConcurrentDictionary<Guid, bool> PreviousStates = new();

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("ShowFloatingWindowAction OnInvoke 开始");

        try
        {
            var shouldShow = Settings.ShowFloatingWindow;
            var config = GlobalConstants.MainConfig?.Data;

            // 如果没有可用的悬浮窗组件，则强制隐藏且不允许显示
            if (_floatingWindowService.Entries.Count == 0)
            {
                shouldShow = false;
                _logger.LogDebug("没有可用的悬浮窗组件，强制隐藏悬浮窗");
            }

            if (IsRevertable && config != null)
            {
                PreviousStates[ActionSet.Guid] = config.ShowFloatingWindow;
            }

            if (config != null)
            {
                config.ShowFloatingWindow = shouldShow;
                GlobalConstants.MainConfig?.Save();
            }

            _floatingWindowService.UpdateWindowState();

            _logger.LogInformation("悬浮窗状态已更新为: {State}", shouldShow ? "开启" : "关闭");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新悬浮窗状态失败");
            throw;
        }

        await base.OnInvoke();
        _logger.LogDebug("ShowFloatingWindowAction OnInvoke 完成");
    }

    protected override async Task OnRevert()
    {
        await base.OnRevert();

        if (!PreviousStates.TryRemove(ActionSet.Guid, out var previousState))
        {
            _logger.LogInformation("未找到恢复快照，跳过悬浮窗恢复。ActionSet={ActionSetGuid}", ActionSet.Guid);
            return;
        }

        GlobalConstants.MainConfig!.Data.ShowFloatingWindow = previousState;
        GlobalConstants.MainConfig.Save();
        _floatingWindowService.UpdateWindowState();

        _logger.LogInformation("已恢复悬浮窗状态为: {State}", previousState ? "开启" : "关闭");
    }
}
