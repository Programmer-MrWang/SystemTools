using System;
using System.Diagnostics;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using SystemTools.Services;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.InternalDisplay", "仅电脑屏幕", "\uE62F", false)]
public class InternalDisplayAction(ILogger<InternalDisplayAction> logger, IProcessRunner processRunner) : ActionBase
{
    private readonly ILogger<InternalDisplayAction> _logger = logger;
    private readonly IProcessRunner _processRunner = processRunner;

    protected override async Task OnInvoke()
    {
        try
        {
            _logger.LogInformation("正在执行仅电脑屏幕命令");

            var processInfo = new ProcessStartInfo
            {
                FileName = "DisplaySwitch.exe",
                Arguments = "/internal",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            await _processRunner.RunAsync(processInfo, "仅电脑屏幕(DisplaySwitch)");
            _logger.LogInformation("仅电脑屏幕命令执行完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "仅电脑屏幕失败");
            throw;
        }

        await base.OnInvoke();
    }
}
