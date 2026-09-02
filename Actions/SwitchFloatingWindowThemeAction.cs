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
using SystemTools.Shared;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.SwitchFloatingWindowTheme", "切换悬浮窗主题", "\uE790", false)]
public class SwitchFloatingWindowThemeAction(ILogger<SwitchFloatingWindowThemeAction> logger) : ActionBase<SwitchFloatingWindowThemeSettings>
{
    private readonly ILogger<SwitchFloatingWindowThemeAction> _logger = logger;
    private static readonly ConcurrentDictionary<Guid, int> PreviousThemes = new();

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("SwitchFloatingWindowThemeAction OnInvoke 开始");

        try
        {
            var service = IAppHost.GetService<FloatingWindowService>();
            var config = GlobalConstants.MainConfig?.Data;

            if (Settings.TargetTheme >= 0)
            {
                if (IsRevertable && config != null)
                {
                    PreviousThemes[ActionSet.Guid] = config.FloatingWindowTheme;
                }

                service.SetWindowTheme(Settings.TargetTheme);
                _logger.LogInformation("已设置悬浮窗主题为: {Theme}", GetThemeName(Settings.TargetTheme));
            }
            else
            {
                if (IsRevertable && config != null)
                {
                    PreviousThemes[ActionSet.Guid] = config.FloatingWindowTheme;
                }

                service.ToggleWindowTheme();
                _logger.LogInformation("已切换到下一个悬浮窗主题");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "切换悬浮窗主题失败");
            throw;
        }
        if (Settings.NotifyOnExecute)
            IAppHost.GetService<SystemToolsNotificationProvider>()?.ShowNotification(new NotificationRequest
            {
                MaskContent = NotificationContent.CreateTwoIconsMask("已自动切换悬浮窗主题", "\uE9FB", "")
            });


        await base.OnInvoke();
        _logger.LogDebug("SwitchFloatingWindowThemeAction OnInvoke 完成");
    }

    protected override async Task OnRevert()
    {
        await base.OnRevert();

        if (!PreviousThemes.TryRemove(ActionSet.Guid, out var previousTheme))
        {
            _logger.LogInformation("未找到主题恢复快照，跳过悬浮窗主题恢复。ActionSet={ActionSetGuid}", ActionSet.Guid);
            return;
        }

        try
        {
            var service = IAppHost.GetService<FloatingWindowService>();
            service.SetWindowTheme(previousTheme);
            _logger.LogInformation("已恢复悬浮窗主题为: {Theme}", GetThemeName(previousTheme));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "恢复悬浮窗主题失败");
            throw;
        }
    }

    private static string GetThemeName(int theme)
    {
        return theme switch
        {
            0 => "跟随系统",
            1 => "浅色",
            2 => "深色",
            3 => "自适应背景",
            _ => "未知"
        };
    }
}
