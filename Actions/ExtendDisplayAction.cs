using System;
using System.Diagnostics;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using SystemTools.Services;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.ExtendDisplay", "扩展屏幕", "\uE647", false)]
public class ExtendDisplayAction(ILogger<ExtendDisplayAction> logger, IProcessRunner processRunner) : ActionBase
{
    private readonly ILogger<ExtendDisplayAction> _logger = logger;
    private readonly IProcessRunner _processRunner = processRunner;

    protected override async Task OnInvoke()
    {
        try
        {
            _logger.LogInformation("正在执行扩展屏幕命令");

            var processInfo = new ProcessStartInfo
            {
                FileName = "DisplaySwitch.exe",
                Arguments = "/extend",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            await _processRunner.RunAsync(processInfo, "扩展屏幕(DisplaySwitch)");
            _logger.LogInformation("扩展屏幕命令执行完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "扩展屏幕失败");
            throw;
        }

        await base.OnInvoke();
    }
}
