using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using SystemTools.Settings;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.KillProcess", "退出进程", "\uE0DE", false)]
public class KillProcessAction(ILogger<KillProcessAction> logger) : ActionBase<KillProcessSettings>
{
    private readonly ILogger<KillProcessAction> _logger = logger;

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("KillProcessAction OnInvoke 开始");

        if (Settings == null || string.IsNullOrWhiteSpace(Settings.ProcessName))
        {
            _logger.LogWarning("进程名为空");
            return;
        }

        try
        {
            var processName = Settings.ProcessName.Trim();
            if (!processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                processName += ".exe";
            }

            _logger.LogInformation("正在终止进程: {ProcessName}", processName);

            var psi = new ProcessStartInfo
            {
                FileName = "taskkill",
                Arguments = $"/f /im \"{processName}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(psi);
            if (process == null) throw new Exception("无法启动 taskkill 进程");

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                _logger.LogInformation("进程终止成功: {ProcessName}", processName);
            }
            else
            {
                _logger.LogWarning("终止进程 {ProcessName} 可能失败，退出码: {ExitCode}, 错误: {Error}",
                    processName, process.ExitCode, error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "终止进程失败");
            throw;
        }

        await base.OnInvoke();
        _logger.LogDebug("KillProcessAction OnInvoke 完成");
    }
}