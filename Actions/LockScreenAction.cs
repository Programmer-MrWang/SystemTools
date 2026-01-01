using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.LockScreen", "锁定屏幕", "\uEAF0", false)]
public class LockScreenAction : ActionBase
{
    private readonly ILogger<LockScreenAction> _logger;

    public LockScreenAction(ILogger<LockScreenAction> logger)
    {
        _logger = logger;
    }

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("LockScreenAction OnInvoke 开始");

        try
        {
            _logger.LogInformation("正在执行锁定屏幕命令");

            var psi = new ProcessStartInfo
            {
                FileName = "rundll32.exe",
                Arguments = "user32.dll,LockWorkStation",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process.Start(psi);

            _logger.LogInformation("屏幕已锁定");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "锁定屏幕失败");
            throw;
        }

        await base.OnInvoke();
        _logger.LogDebug("LockScreenAction OnInvoke 完成");
    }
}