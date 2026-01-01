using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using SystemTools.Settings;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.Shutdown", "计时关机", "\uE4C4",false)]
public class ShutdownAction : ActionBase<ShutdownSettings>
{
    private readonly ILogger<ShutdownAction> _logger;
    private readonly string _filePath;

    public ShutdownAction(ILogger<ShutdownAction> logger)
    {
        _logger = logger;
        var pluginDir = Path.GetDirectoryName(GetType().Assembly.Location);
        _filePath = Path.Combine(pluginDir, "shutdown.json");
    }

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("ShutdownAction OnInvoke 开始");

        //var settings = await LoadSettingsAsync();
        if (Settings == null) return;

        if (Settings.Seconds < 0) return;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "shutdown",
                Arguments = $"-s -t {Settings.Seconds}",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);

            if (!Settings.ShowPrompt)
            {
                await Task.Delay(300);
                SendKeys.SendWait("{ENTER}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行关机失败");
            throw;
        }

        await base.OnInvoke();
    }

    /*private async Task<ShutdownSettings> LoadSettingsAsync()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath);
                return JsonSerializer.Deserialize<ShutdownSettings>(json);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取 shutdown.json 失败");
        }
        return null;
    }*/
}