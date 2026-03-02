using System;
using System.Diagnostics;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using SystemTools.Services;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.CloneDisplay", "复制屏幕", "\uE635", false)]
public class CloneDisplayAction(ILogger<CloneDisplayAction> logger, IProcessRunner processRunner) : ActionBase
{
    private readonly ILogger<CloneDisplayAction> _logger = logger;
    private readonly IProcessRunner _processRunner = processRunner;

    protected override async Task OnInvoke()
    {
        try
        {
            _logger.LogInformation("正在执行复制屏幕命令");

            var processInfo = new ProcessStartInfo
            {
                FileName = "DisplaySwitch.exe",
                Arguments = "/clone",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            await _processRunner.RunAsync(processInfo, "复制屏幕(DisplaySwitch)");
            _logger.LogInformation("复制屏幕命令执行完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "复制屏幕失败");
            throw;
        }

        await base.OnInvoke();
    }
}
