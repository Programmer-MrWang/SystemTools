using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.OpenClassSwapWindow", "打开换课窗口", "\uE13B", false)]
public class OpenClassSwapWindowAction(
    ILogger<OpenClassSwapWindowAction> logger,
    IUriNavigationService uriNavigationService) : ActionBase
{
    private readonly ILogger<OpenClassSwapWindowAction> _logger = logger;
    private readonly IUriNavigationService _uriNavigationService = uriNavigationService;

    protected override Task OnInvoke()
    {
        _logger.LogInformation("正在打开 ClassIsland 换课窗口");
        _uriNavigationService.NavigateWrapped(new Uri("classisland://app/class-swap"));
        return base.OnInvoke();
    }
}
