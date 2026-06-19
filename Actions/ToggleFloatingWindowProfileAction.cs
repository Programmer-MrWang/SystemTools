using System;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using Microsoft.Extensions.Logging;
using SystemTools.Services;
using SystemTools.Settings;

namespace SystemTools.Actions;

/// <summary>
/// 切换悬浮窗配置方案行动
/// </summary>
[ActionInfo("SystemTools.ToggleFloatingWindowProfile", "切换悬浮窗配置方案", "\uE9A8", false)]
public class ToggleFloatingWindowProfileAction(ILogger<ToggleFloatingWindowProfileAction> logger) : ActionBase<ToggleFloatingWindowProfileSettings>
{
    private readonly ILogger<ToggleFloatingWindowProfileAction> _logger = logger;

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("ToggleFloatingWindowProfileAction OnInvoke 开始");

        try
        {
            var service = IAppHost.GetService<FloatingWindowService>();

            // 根据设置决定是切换到下一个还是切换到指定方案
            // TargetProfileName: null=切换到下一个, 其他=指定方案名称
            if (!string.IsNullOrWhiteSpace(Settings.TargetProfileName))
            {
                service.SwitchToProfile(Settings.TargetProfileName);
                _logger.LogInformation("已切换到悬浮窗配置方案: {Name}", Settings.TargetProfileName);
            }
            else
            {
                service.ToggleWindowProfile();
                _logger.LogInformation("已切换到下一个悬浮窗配置方案");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "切换悬浮窗配置方案失败");
            throw;
        }

        await base.OnInvoke();
        _logger.LogDebug("ToggleFloatingWindowProfileAction OnInvoke 完成");
    }
}
