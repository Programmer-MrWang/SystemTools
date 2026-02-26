using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using SystemTools.Settings;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.SwitchTheme", "切换主题色", "\uF42F", false)]
public class SwitchThemeAction(ILogger<SwitchThemeAction> logger) : ActionBase<ThemeSettings>
{
    private readonly ILogger<SwitchThemeAction> _logger = logger;

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("SwitchThemeAction OnInvoke 开始");

        if (Settings == null) return;

        try
        {
            var regValue = Settings.Theme == "浅色" ? 1 : 0;

            using RegistryKey registryKey =
                Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            registryKey.SetValue("AppsUseLightTheme", regValue, RegistryValueKind.DWord);
            _logger.LogInformation("切换主题成功");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "切换主题失败");
            throw;
        }

        await base.OnInvoke();
    }
}