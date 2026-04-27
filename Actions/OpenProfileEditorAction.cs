using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.OpenProfileEditor", "打开档案编辑", "\uE699", false)]
public class OpenProfileEditorAction(
    ILogger<OpenProfileEditorAction> logger,
    IUriNavigationService uriNavigationService) : ActionBase
{
    private readonly ILogger<OpenProfileEditorAction> _logger = logger;
    private readonly IUriNavigationService _uriNavigationService = uriNavigationService;

    protected override Task OnInvoke()
    {
        _logger.LogInformation("正在打开 ClassIsland 档案编辑窗口");
        _uriNavigationService.NavigateWrapped(new Uri("classisland://app/profile"));
        return base.OnInvoke();
    }
}
