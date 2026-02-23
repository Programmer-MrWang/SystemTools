using System;
using System.Diagnostics;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;

namespace SystemTools.Actions;

/// <summary>
/// 仅第二屏幕
/// </summary>
[ActionInfo("SystemTools.ExternalDisplay", "仅第二屏幕", "\uE641", false)]
public class ExternalDisplayAction(ILogger<ExternalDisplayAction> logger) : ActionBase
{
    private readonly ILogger<ExternalDisplayAction> _logger = logger;

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
                RedirectStandardError = true
            };

            using var process = Process.Start(processInfo);
            if (process != null)
            {
                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                _logger.LogInformation("仅第二屏幕命令已执行，退出码: {ExitCode}", process.ExitCode);

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
            _logger.LogError(ex, "仅第二屏幕失败");
            throw;
        }

        await base.OnInvoke();
    }
}