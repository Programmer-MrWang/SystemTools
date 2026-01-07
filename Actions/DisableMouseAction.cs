using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.DisableMouse", "禁用鼠标", "\uE5C7", false)]
public class DisableMouseAction : ActionBase
{
    private readonly ILogger<DisableMouseAction> _logger;

    public DisableMouseAction(ILogger<DisableMouseAction> logger)
    {
        _logger = logger;
    }

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("DisableMouseAction OnInvoke 开始");

        try
        {
            var pluginDir = Path.GetDirectoryName(GetType().Assembly.Location);
            var batchPath = Path.Combine(pluginDir, "jinyongshubiao.bat");

            if (!File.Exists(batchPath))
            {
                _logger.LogError("找不到禁用鼠标批处理文件: {Path}", batchPath);
                throw new FileNotFoundException($"找不到禁用鼠标批处理文件: {batchPath}");
            }

            _logger.LogInformation("正在运行禁用鼠标批处理: {Path}", batchPath);

            var psi = new ProcessStartInfo
            {
                FileName = batchPath,
                WorkingDirectory = pluginDir,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                throw new Exception("无法启动批处理进程");
            }

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            _logger.LogInformation("禁用鼠标批处理执行完成，退出码: {ExitCode}", process.ExitCode);
            if (!string.IsNullOrWhiteSpace(output))
                _logger.LogDebug("批处理输出: {Output}", output);
            if (!string.IsNullOrWhiteSpace(error))
                _logger.LogWarning("批处理错误: {Error}", error);

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("批处理返回非零退出码: {ExitCode}", process.ExitCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "禁用鼠标失败");
            throw;
        }

        await base.OnInvoke();
        _logger.LogDebug("DisableMouseAction OnInvoke 完成");
    }
}