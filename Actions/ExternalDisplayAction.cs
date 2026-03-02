using System;
using System.Diagnostics;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using SystemTools.Services;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.ExternalDisplay", "仅第二屏幕", "\uE641", false)]
public class ExternalDisplayAction(ILogger<ExternalDisplayAction> logger, IProcessRunner processRunner) : ActionBase
{
    private readonly ILogger<ExternalDisplayAction> _logger = logger;
    private readonly IProcessRunner _processRunner = processRunner;

    protected override async Task OnInvoke()
    {
        try
        {
            _logger.LogInformation("正在执行仅第二屏幕命令");

            var processInfo = new ProcessStartInfo
            {
                FileName = "DisplaySwitch.exe",
                Arguments = "/external",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            await _processRunner.RunAsync(processInfo, "仅第二屏幕(DisplaySwitch)");
            _logger.LogInformation("仅第二屏幕命令执行完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "仅第二屏幕失败");
            throw;
        }

        await base.OnInvoke();
    }
}
