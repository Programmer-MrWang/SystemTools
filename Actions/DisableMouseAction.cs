using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using SystemTools.Services;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.DisableMouse", "禁用鼠标", "\uE5C7", false)]
public class DisableMouseAction(ILogger<DisableMouseAction> logger, IProcessRunner processRunner) : ActionBase
{
    private readonly ILogger<DisableMouseAction> _logger = logger;
    private readonly IProcessRunner _processRunner = processRunner;

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("DisableMouseAction OnInvoke 开始");

        try
        {
            string? pluginDir = Path.GetDirectoryName(GetType().Assembly.Location);
            if (string.IsNullOrEmpty(pluginDir))
            {
                _logger.LogError("无法获取程序集位置");
                throw new FileNotFoundException("无法获取程序集位置");
            }

            var batchPath = Path.Combine(pluginDir, "jinyongshubiao.bat");

            if (!File.Exists(batchPath))
            {
                _logger.LogError("找不到禁用鼠标批处理文件: {Path}", batchPath);
                throw new FileNotFoundException($"找不到禁用鼠标批处理文件: {batchPath}");
            }

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

            await _processRunner.RunAsync(psi, "禁用鼠标批处理", timeout: TimeSpan.FromMinutes(2));
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
