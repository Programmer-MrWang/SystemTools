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

[ActionInfo("SystemTools.AutoHideMainWindowWhenOccluded", "遮挡文字时隐藏主界面", "\uEEE3", false)]
public class AutoHideMainWindowWhenOccludedAction(ILogger<AutoHideMainWindowWhenOccludedAction> logger) : ActionBase<AutoHideMainWindowWhenOccludedActionSettings>
{
    private readonly ILogger<AutoHideMainWindowWhenOccludedAction> _logger = logger;

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("AutoHideMainWindowWhenOccludedAction OnInvoke 开始");

        if (Settings == null) return;

        var config = GlobalConstants.MainConfig?.Data;
        if (config == null) return;

        try
        {
            config.AutoHideMainWindowWhenOccluded = Settings.Enable;
            IAppHost.GetService<MainWindowTextOcclusionService>().ApplyConfig();
            GlobalConstants.MainConfig?.Save();
            _logger.LogInformation("已{State}遮挡文字时隐藏主界面功能", Settings.Enable ? "开启" : "关闭");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "设置遮挡文字时隐藏主界面功能失败");
            throw;
        }

        if (Settings.NotifyOnExecute)
        {
            IAppHost.GetService<SystemToolsNotificationProvider>()?.ShowNotification(new NotificationRequest
            {
                MaskContent = NotificationContent.CreateTwoIconsMask(
                    (Settings.Enable ? "已开启功能 " : "已关闭功能 ") + "遮挡文字时隐藏主界面", "\uEEE3", "")
            });
        }

        await base.OnInvoke();
        _logger.LogDebug("AutoHideMainWindowWhenOccludedAction OnInvoke 完成");
    }
}