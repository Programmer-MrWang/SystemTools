using System;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Models.Notification;
using ClassIsland.Shared;
using Microsoft.Extensions.Logging;
using SystemTools.Services;
using SystemTools.Settings;
using SystemTools.Shared;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.AutoSwitchClassIslandTheme", "自动切换 ClassIsland 主题", "\uE5CB", false)]
public class AutoSwitchClassIslandThemeAction(ILogger<AutoSwitchClassIslandThemeAction> logger) : ActionBase<AutoSwitchClassIslandThemeActionSettings>
{
    private readonly ILogger<AutoSwitchClassIslandThemeAction> _logger = logger;

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("AutoSwitchClassIslandThemeAction OnInvoke 开始");

        if (Settings == null) return;

        var config = GlobalConstants.MainConfig?.Data;
        if (config == null) return;

        try
        {
            config.AutoSwitchClassIslandTheme = Settings.Enable;
            IAppHost.GetService<AdaptiveThemeSyncService>().ApplyConfig();
            GlobalConstants.MainConfig?.Save();
            _logger.LogInformation("已{State}自动切换 ClassIsland 主题功能", Settings.Enable ? "开启" : "关闭");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "设置自动切换 ClassIsland 主题功能失败");
            throw;
        }

        if (Settings.NotifyOnExecute)
        {
            IAppHost.GetService<SystemToolsNotificationProvider>()?.ShowNotification(new NotificationRequest
            {
                MaskContent = NotificationContent.CreateTwoIconsMask(
                    (Settings.Enable ? "已开启功能 " : "已关闭功能 ") + "自动切换 ClassIsland 主题", "\uE5CB", "")
            });
        }

        await base.OnInvoke();
        _logger.LogDebug("AutoSwitchClassIslandThemeAction OnInvoke 完成");
    }
}