using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using Microsoft.Extensions.Logging;
using SystemTools.Services;
using SystemTools.Settings;
using ClassIsland.Core.Models.Notification;

namespace SystemTools.Actions;

/// <summary>
/// 切换悬浮窗配置方案行动
/// </summary>
[ActionInfo("SystemTools.ToggleFloatingWindowProfile", "切换悬浮窗配置方案", "\uE9A8", false)]
public class ToggleFloatingWindowProfileAction(ILogger<ToggleFloatingWindowProfileAction> logger) : ActionBase<ToggleFloatingWindowProfileSettings>
{
    private readonly ILogger<ToggleFloatingWindowProfileAction> _logger = logger;
    private static readonly ConcurrentDictionary<Guid, string> PreviousProfiles = new();

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("ToggleFloatingWindowProfileAction OnInvoke 开始");

        try
        {
            var service = IAppHost.GetService<FloatingWindowService>();
            var currentProfileName = service.ProfileManager.CurrentProfileName;

            // 根据设置决定是切换到下一个还是切换到指定方案
            // TargetProfileName: null=切换到下一个, 其他=指定方案名称
            if (!string.IsNullOrWhiteSpace(Settings.TargetProfileName))
            {
                if (IsRevertable)
                {
                    PreviousProfiles[ActionSet.Guid] = currentProfileName;
                }

                service.SwitchToProfile(Settings.TargetProfileName);
                _logger.LogInformation("已切换到悬浮窗配置方案: {Name}", Settings.TargetProfileName);
            }
            else
            {
                if (IsRevertable)
                {
                    PreviousProfiles[ActionSet.Guid] = currentProfileName;
                }

                service.ToggleWindowProfile();
                _logger.LogInformation("已切换到下一个悬浮窗配置方案");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "切换悬浮窗配置方案失败");
            throw;
        }
        if (Settings.NotifyOnExecute)
            IAppHost.GetService<SystemToolsNotificationProvider>()?.ShowNotification(new NotificationRequest
            {
                MaskContent = NotificationContent.CreateTwoIconsMask("已自动切换悬浮窗配置方案", "\uE9FB", "")
            });


        await base.OnInvoke();
        _logger.LogDebug("ToggleFloatingWindowProfileAction OnInvoke 完成");
    }

    protected override async Task OnRevert()
    {
        await base.OnRevert();

        if (!PreviousProfiles.TryRemove(ActionSet.Guid, out var previousProfile))
        {
            _logger.LogInformation("未找到配置方案恢复快照，跳过悬浮窗配置方案恢复。ActionSet={ActionSetGuid}", ActionSet.Guid);
            return;
        }

        try
        {
            var service = IAppHost.GetService<FloatingWindowService>();
            service.SwitchToProfile(previousProfile);
            _logger.LogInformation("已恢复悬浮窗配置方案为: {Name}", previousProfile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "恢复悬浮窗配置方案失败");
            throw;
        }
    }
}
