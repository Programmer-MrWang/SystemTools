using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.CancelShutdown", "取消关机计划", "\uE4CC", false)]
public class CancelShutdownAction(ILogger<CancelShutdownAction> logger) : ActionBase
{
    private readonly ILogger<CancelShutdownAction> _logger = logger;

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("CancelShutdownAction OnInvoke 开始");

        try
        {
            _logger.LogInformation("正在执行取消关机命令");

            var psi = new ProcessStartInfo
            {
                FileName = "shutdown",
                Arguments = "-a",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process.Start(psi);

            _logger.LogInformation("关机已取消");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取消关机失败");
            throw;
        }

        await base.OnInvoke();
        _logger.LogDebug("CancelShutdownAction OnInvoke 完成");
    }
}