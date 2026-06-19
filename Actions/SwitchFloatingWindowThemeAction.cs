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
/// 切换悬浮窗主题行动
/// </summary>
[ActionInfo("SystemTools.SwitchFloatingWindowTheme", "切换悬浮窗主题", "\uE790", false)]
public class SwitchFloatingWindowThemeAction(ILogger<SwitchFloatingWindowThemeAction> logger) : ActionBase<SwitchFloatingWindowThemeSettings>
{
    private readonly ILogger<SwitchFloatingWindowThemeAction> _logger = logger;

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("SwitchFloatingWindowThemeAction OnInvoke 开始");

        try
        {
            var service = IAppHost.GetService<FloatingWindowService>();

            if (Settings.TargetTheme >= 0)
            {
                service.SetWindowTheme(Settings.TargetTheme);
                _logger.LogInformation("已设置悬浮窗主题为: {Theme}", GetThemeName(Settings.TargetTheme));
            }
            else
            {
                service.ToggleWindowTheme();
                _logger.LogInformation("已切换到下一个悬浮窗主题");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "切换悬浮窗主题失败");
            throw;
        }

        await base.OnInvoke();
        _logger.LogDebug("SwitchFloatingWindowThemeAction OnInvoke 完成");
    }

    private static string GetThemeName(int theme)
    {
        return theme switch
        {
            0 => "跟随系统",
            1 => "浅色",
            2 => "深色",
            _ => "未知"
        };
    }
}
