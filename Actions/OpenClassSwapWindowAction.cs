using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using ClassIsland.Core.Models.Notification;
using SystemTools.Services;
using SystemTools.Settings;
using ClassIsland.Shared;
using System;
using System.Threading.Tasks;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.OpenClassSwapWindow", "打开换课窗口", "\uE13B", false)]
public class OpenClassSwapWindowAction(
    ILogger<OpenClassSwapWindowAction> logger,
    IUriNavigationService uriNavigationService) : ActionBase<ShortcutKeyNotificationSettings>
{
    private readonly ILogger<OpenClassSwapWindowAction> _logger = logger;
    private readonly IUriNavigationService _uriNavigationService = uriNavigationService;

    protected override Task OnInvoke()
    {
        _logger.LogInformation("正在打开 ClassIsland 换课窗口");
        _uriNavigationService.NavigateWrapped(new Uri("classisland://app/class-swap"));
        if (Settings.NotifyOnExecute)
            IAppHost.GetService<SystemToolsNotificationProvider>()?.ShowNotification(new NotificationRequest
            {
                MaskContent = NotificationContent.CreateTwoIconsMask("已自动打开换课窗口", "\uE9FB", "")
            });

        return base.OnInvoke();
    }
}
