using System;
using System.Diagnostics;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;

namespace SystemTools.Actions;

/// <summary>
/// 仅电脑屏幕
/// </summary>
[ActionInfo("SystemTools.InternalDisplay", "仅电脑屏幕", "\uE62F", false)]
public class InternalDisplayAction : ActionBase
{
    private readonly ILogger<InternalDisplayAction> _logger;

    public InternalDisplayAction(ILogger<InternalDisplayAction> logger)
    {
        _logger = logger;
    }

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
                RedirectStandardError = true
            };

            using var process = Process.Start(processInfo);
            if (process != null)
            {
                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                _logger.LogInformation("仅电脑屏幕命令已执行，退出码: {ExitCode}", process.ExitCode);

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
            _logger.LogError(ex, "仅电脑屏幕失败");
            throw;
        }

        await base.OnInvoke();
    }
}