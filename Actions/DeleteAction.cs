using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using SystemTools.Services;
using SystemTools.Settings;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.Delete", "删除", "\uE61D", false)]
public class DeleteAction(ILogger<DeleteAction> logger, IProcessRunner processRunner) : ActionBase<DeleteSettings>
{
    private readonly ILogger<DeleteAction> _logger = logger;
    private readonly IProcessRunner _processRunner = processRunner;

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("DeleteAction OnInvoke 开始");

        if (Settings == null || string.IsNullOrWhiteSpace(Settings.TargetPath))
        {
            _logger.LogWarning("路径为空");
            return;
        }

        try
        {
            var targetPath = Settings.TargetPath.TrimEnd('\\');

            if (Settings.OperationType == "文件")
            {
                if (!File.Exists(targetPath))
                {
                    _logger.LogError("文件不存在: {Path}", targetPath);
                    throw new FileNotFoundException("文件不存在", targetPath);
                }

                await Task.Run(() => File.Delete(targetPath));
                _logger.LogInformation("文件删除成功: {Path}", targetPath);
            }
            else
            {
                if (!Directory.Exists(targetPath))
                {
                    _logger.LogError("文件夹不存在: {Path}", targetPath);
                    throw new DirectoryNotFoundException($"文件夹不存在: {targetPath}");
                }

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c rmdir /s /q \"{targetPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                await _processRunner.RunAsync(psi, "删除文件夹(rmdir)", timeout: TimeSpan.FromMinutes(10));
                _logger.LogInformation("文件夹删除成功: {Path}", targetPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除失败");
            throw;
        }

        await base.OnInvoke();
        _logger.LogDebug("DeleteAction OnInvoke 完成");
    }
}
