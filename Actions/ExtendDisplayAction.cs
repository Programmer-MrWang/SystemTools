using System;
using System.Diagnostics;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;

namespace SystemTools.Actions;

/// <summary>
/// 扩展屏幕
/// </summary>
[ActionInfo("SystemTools.ExtendDisplay", "扩展屏幕", "\uE647", false)]
public class ExtendDisplayAction : ActionBase
{
    private readonly ILogger<ExtendDisplayAction> _logger;

    public ExtendDisplayAction(ILogger<ExtendDisplayAction> logger)
    {
        _logger = logger;
    }

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
                RedirectStandardError = true
            };

            using var process = Process.Start(processInfo);
            if (process != null)
            {
                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                _logger.LogInformation("扩展屏幕命令已执行，退出码: {ExitCode}", process.ExitCode);

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
            _logger.LogError(ex, "扩展屏幕失败");
            throw;
        }

        await base.OnInvoke();
    }
}