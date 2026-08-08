using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using ClassIsland.Core.Models.Notification;
using SystemTools.Services;
using SystemTools.Settings;
using ClassIsland.Shared;
using System.Reflection;
using System.Threading.Tasks;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.ClearAllNotifications", "清除全部提醒", "\uE029", false)]
public class ClearAllNotificationsAction(ILogger<ClearAllNotificationsAction> logger) : ActionBase<ShortcutKeyNotificationSettings>
{
    private readonly ILogger<ClearAllNotificationsAction> _logger = logger;

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("ClearAllNotificationsAction OnInvoke 开始");

        var notificationHostService = IAppHost.GetService<ClassIsland.Core.Abstractions.Services.INotificationHostService>();
        var method = notificationHostService.GetType().GetMethod("CancelAllNotifications",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (method == null)
        {
            _logger.LogWarning("未找到 CancelAllNotifications 方法，无法清除提醒");
            return;
        }

        method.Invoke(notificationHostService, null);
        _logger.LogInformation("已调用 ClassIsland 提醒系统的清除全部提醒");
        if (Settings.NotifyOnExecute)
            IAppHost.GetService<SystemToolsNotificationProvider>()?.ShowNotification(new NotificationRequest
            {
                MaskContent = NotificationContent.CreateTwoIconsMask("已自动清除全部提醒", "\uE9FB", "")
            });


        await base.OnInvoke();
        _logger.LogDebug("ClearAllNotificationsAction OnInvoke 完成");
    }
}
