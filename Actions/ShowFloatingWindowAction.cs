using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using SystemTools.Services;
using SystemTools.Settings;
using ClassIsland.Core.Models.Notification;
using ClassIsland.Shared;
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
        if (Settings.NotifyOnExecute)
            IAppHost.GetService<SystemToolsNotificationProvider>()?.ShowNotification(new NotificationRequest
            {
                MaskContent = NotificationContent.CreateTwoIconsMask("已自动显示悬浮窗", "\uE9FB", "")
            });


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

        var config = GlobalConstants.MainConfig;
        if (config == null)
        {
            _logger.LogWarning("主配置为空，无法恢复悬浮窗状态");
            return;
        }

        config.Data.ShowFloatingWindow = previousState;
        config.Save();
        _floatingWindowService.UpdateWindowState();

        _logger.LogInformation("已恢复悬浮窗状态为: {State}", previousState ? "开启" : "关闭");
    }
}
