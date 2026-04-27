using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.OpenAppSettings", "打开应用设置", "\uEF27", false)]
public class OpenAppSettingsAction(
    ILogger<OpenAppSettingsAction> logger,
    IUriNavigationService uriNavigationService) : ActionBase
{
    private readonly ILogger<OpenAppSettingsAction> _logger = logger;
    private readonly IUriNavigationService _uriNavigationService = uriNavigationService;

    protected override Task OnInvoke()
    {
        _logger.LogInformation("正在打开 ClassIsland 应用设置窗口");
        _uriNavigationService.NavigateWrapped(new Uri("classisland://app/settings"));
        return base.OnInvoke();
    }
}
