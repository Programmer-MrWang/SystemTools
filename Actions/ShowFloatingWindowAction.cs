using System;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using SystemTools.Services;
using SystemTools.Settings;
using SystemTools.Shared;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.ShowFloatingWindow", "显示悬浮窗", "\uEA37", false)]
public class ShowFloatingWindowAction(
    ILogger<ShowFloatingWindowAction> logger,
    FloatingWindowService floatingWindowService) : ActionBase<ShowFloatingWindowSettings>
{
    private readonly ILogger<ShowFloatingWindowAction> _logger = logger;
    private readonly FloatingWindowService _floatingWindowService = floatingWindowService;

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("ShowFloatingWindowAction OnInvoke 开始");

        try
        {
            var shouldShow = Settings.ShowFloatingWindow;
            GlobalConstants.MainConfig!.Data.ShowFloatingWindow = shouldShow;
            GlobalConstants.MainConfig.Save();
            _floatingWindowService.UpdateWindowState();

            _logger.LogInformation("悬浮窗状态已更新为: {State}", shouldShow ? "开启" : "关闭");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新悬浮窗状态失败");
            throw;
        }

        await base.OnInvoke();
        _logger.LogDebug("ShowFloatingWindowAction OnInvoke 完成");
    }
}
