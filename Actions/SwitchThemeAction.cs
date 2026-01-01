using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using SystemTools.Settings;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.SwitchTheme", "切换主题色", "\uF42F",false)]
public class SwitchThemeAction : ActionBase
{
    private readonly ILogger<SwitchThemeAction> _logger;
    private readonly string _filePath;

    public SwitchThemeAction(ILogger<SwitchThemeAction> logger)
    {
        _logger = logger;
        var pluginDir = Path.GetDirectoryName(GetType().Assembly.Location);
        _filePath = Path.Combine(pluginDir, "themes.json");
    }

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("SwitchThemeAction OnInvoke 开始");

        var settings = await LoadSettingsAsync();
        if (settings == null) return;

        try
        {
            var regValue = settings.Theme == "浅色" ? "1" : "0";
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

    private async Task<ThemeSettings> LoadSettingsAsync()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath);
                return JsonSerializer.Deserialize<ThemeSettings>(json);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取 themes.json 失败");
        }
        return null;
    }
}