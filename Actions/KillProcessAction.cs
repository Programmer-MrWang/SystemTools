using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Net.Http;
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


        var processName = Settings.ProcessName.Trim();
        if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            processName = processName[..^4]; //等价于processName.Remove(processName.Length - 4)
        }

        _logger.LogInformation("正在终止进程: {ProcessName}", processName);
        Process[] pses;
        try
        {
            pses = Process.GetProcessesByName(processName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取相关联的进程列表失败");
            throw;
        }
        foreach (var ps in pses)
        {
            try
            {
                ps.Kill();
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "终止进程 {ProcessName} (PID: {PID}) 失败", ps.ProcessName, ps.Id);
                continue;
            }
        }

        //var psi = new ProcessStartInfo
        //{
        //    FileName = "taskkill",
        //    Arguments = $"/f /im \"{processName}\"",
        //    CreateNoWindow = true,
        //    UseShellExecute = false,
        //    RedirectStandardOutput = true,
        //    RedirectStandardError = true,
        //    WindowStyle = ProcessWindowStyle.Hidden
        //};

        //using var process = Process.Start(psi) ?? throw new Exception("无法启动 taskkill 进程");
        //string output = await process.StandardOutput.ReadToEndAsync();
        //string error = await process.StandardError.ReadToEndAsync();
        //await process.WaitForExitAsync();

        //if (process.ExitCode == 0)
        //{
        //    _logger.LogInformation("进程终止成功: {ProcessName}", processName);
        //}
        //else
        //{
        //    _logger.LogWarning("终止进程 {ProcessName} 可能失败，退出码: {ExitCode}, 错误: {Error}",
        //        processName, process.ExitCode, error);
        //}

        await base.OnInvoke();
        _logger.LogDebug("KillProcessAction OnInvoke 完成");
    }
}