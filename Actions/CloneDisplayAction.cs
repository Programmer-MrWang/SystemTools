using System;
using System.Diagnostics;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;

namespace SystemTools.Actions;

/// <summary>
/// 复制屏幕
/// </summary>
[ActionInfo("SystemTools.CloneDisplay", "复制屏幕", "\uE635", false)]
public class CloneDisplayAction : ActionBase
{
    private readonly ILogger<CloneDisplayAction> _logger;

    public CloneDisplayAction(ILogger<CloneDisplayAction> logger)
    {
        _logger = logger;
    }

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
                RedirectStandardError = true
            };

            using var process = Process.Start(processInfo);
            if (process != null)
            {
                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                _logger.LogInformation("复制屏幕命令已执行，退出码: {ExitCode}", process.ExitCode);

                if (!string.IsNullOrEmpty(error))
                    _logger.LogWarning("错误输出: {Error}", error);
            }
            else
            {
                throw new Exception("无法启动 DisplaySwitch.exe 进程");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "复制屏幕失败");
            throw;
        }

        await base.OnInvoke();
    }
}