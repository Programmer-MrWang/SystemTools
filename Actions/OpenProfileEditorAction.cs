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

[ActionInfo("SystemTools.OpenProfileEditor", "打开档案编辑", "\uE699", false)]
public class OpenProfileEditorAction(
    ILogger<OpenProfileEditorAction> logger,
    IUriNavigationService uriNavigationService) : ActionBase<ShortcutKeyNotificationSettings>
{
    private readonly ILogger<OpenProfileEditorAction> _logger = logger;
    private readonly IUriNavigationService _uriNavigationService = uriNavigationService;

    protected override Task OnInvoke()
    {
        _logger.LogInformation("正在打开 ClassIsland 档案编辑窗口");
        _uriNavigationService.NavigateWrapped(new Uri("classisland://app/profile"));
        if (Settings.NotifyOnExecute)
            IAppHost.GetService<SystemToolsNotificationProvider>()?.ShowNotification(new NotificationRequest
            {
                MaskContent = NotificationContent.CreateTwoIconsMask("已自动打开档案编辑", "\uE9FB", "")
            });

        return base.OnInvoke();
    }
}
