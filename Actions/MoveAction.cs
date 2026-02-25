using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using SystemTools.Settings;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.Move", "移动", "\uE6E7", false)]
public class MoveAction(ILogger<MoveAction> logger) : ActionBase<MoveSettings>
{
    private readonly ILogger<MoveAction> _logger = logger;

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("MoveAction OnInvoke 开始");

        if (Settings == null || string.IsNullOrWhiteSpace(Settings.SourcePath) || string.IsNullOrWhiteSpace(Settings.DestinationPath))
        {
            _logger.LogWarning("路径为空");
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            var sourcePath = Settings.SourcePath.TrimEnd('\\');
            var destPath = Settings.DestinationPath.TrimEnd('\\');

            if (Settings.OperationType == "文件")
            {
                if (!File.Exists(sourcePath))
                {
                    _logger.LogError("源文件不存在: {Path}", sourcePath);
                    throw new FileNotFoundException("源文件不存在", sourcePath);
                }

                if (Directory.Exists(destPath))
                {
                    var fileName = Path.GetFileName(sourcePath);
                    destPath = Path.Combine(destPath, fileName);
                }

                var destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                try
                {
                    await Task.Run(() => File.Move(sourcePath, destPath));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "文件移动失败");
                    throw new Exception($"移动失败: {ex}");
                }

                _logger.LogInformation("文件移动成功: {Source} -> {Destination}", sourcePath, destPath);
            }
            else
            {
                if (!Directory.Exists(sourcePath))
                {
                    _logger.LogError("源文件夹不存在: {Path}", sourcePath);
                    throw new DirectoryNotFoundException($"源文件夹不存在: {sourcePath}");
                }

                if (!Directory.Exists(destPath))
                {
                    Directory.CreateDirectory(destPath);
                }

                var sourceDirName = new DirectoryInfo(sourcePath).Name;
                var finalDestPath = Path.Combine(destPath, sourceDirName);

                if (Directory.Exists(finalDestPath))
                {
                    Directory.Delete(finalDestPath, true);
                }

                psi.FileName = "robocopy.exe";
                psi.Arguments = $"\"{sourcePath}\" \"{finalDestPath}\" /e /move /copyall /r:3 /w:3 /mt:4 /nfl /ndl /np";
                _logger.LogInformation("执行命令: robocopy \"{Source}\" \"{Destination}\" /move", sourcePath, finalDestPath);

                using var process = Process.Start(psi) ?? throw new Exception("无法启动进程");
                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode >= 8)
                {
                    _logger.LogError("robocopy 移动失败，退出码: {ExitCode}, 输出: {Output}, 错误: {Error}",
                        process.ExitCode, output, error);
                    throw new Exception($"robocopy 移动失败，退出码: {process.ExitCode}");
                }

                _logger.LogInformation("文件夹移动成功: {Source} -> {Destination}", sourcePath, finalDestPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "移动失败");
            throw;
        }

        await base.OnInvoke();
        _logger.LogDebug("MoveAction OnInvoke 完成");
    }
}
