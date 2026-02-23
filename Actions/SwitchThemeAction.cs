using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
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
            var regValue = Settings.Theme == "浅色" ? "1" : "0";
            var arguments = $@"add ""HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize"" /v AppsUseLightTheme /t REG_DWORD /d {regValue} /f";

            var psi = new ProcessStartInfo
            {
                FileName = "reg",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                Verb = "runas"
            };

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "切换主题失败");
            throw;
        }

        await base.OnInvoke();
    }
}