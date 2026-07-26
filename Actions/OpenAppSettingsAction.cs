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

[ActionInfo("SystemTools.OpenAppSettings", "打开应用设置", "\uEF27", false)]
public class OpenAppSettingsAction(
    ILogger<OpenAppSettingsAction> logger,
    IUriNavigationService uriNavigationService) : ActionBase<ShortcutKeyNotificationSettings>
{
    private readonly ILogger<OpenAppSettingsAction> _logger = logger;
    private readonly IUriNavigationService _uriNavigationService = uriNavigationService;

    protected override Task OnInvoke()
    {
        _logger.LogInformation("正在打开 ClassIsland 应用设置窗口");
        _uriNavigationService.NavigateWrapped(new Uri("classisland://app/settings"));
        if (Settings.NotifyOnExecute)
            IAppHost.GetService<SystemToolsNotificationProvider>()?.ShowNotification(new NotificationRequest
            {
                MaskContent = NotificationContent.CreateTwoIconsMask("已自动打开应用设置", "\uE9FB", "")
            });

        return base.OnInvoke();
    }
}
