using System;
using System.Diagnostics;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.FullscreenClock", "沉浸式时钟", "\uE4D2", false)]
public class FullscreenClockAction(ILogger<FullscreenClockAction> logger) : ActionBase
{
    private readonly ILogger<FullscreenClockAction> _logger = logger;
    private const string ClockUrl = "https://clock.qqhkx.com/";

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("FullscreenClockAction OnInvoke 开始");

        try
        {
            _logger.LogInformation("正在打开沉浸式时钟: {Url}", ClockUrl);

            var psi = new ProcessStartInfo
            {
                FileName = ClockUrl,
                UseShellExecute = true
            };

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "打开沉浸式时钟失败");
            throw;
        }

        await base.OnInvoke();
        _logger.LogDebug("FullscreenClockAction OnInvoke 完成");
    }
}
