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

        if (Settings == null || string.IsNullOrWhiteSpace(Settings.SourcePath) ||
            string.IsNullOrWhiteSpace(Settings.DestinationPath))
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
            // Keep a volume root such as C:\ intact. Trimming it to C: turns
            // it into a drive-relative path and can target the wrong folder.
            var sourcePath = Path.TrimEndingDirectorySeparator(Settings.SourcePath);
            var destPath = Path.TrimEndingDirectorySeparator(Settings.DestinationPath);

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

                var sourceDirectory = new DirectoryInfo(sourcePath);
                if (sourceDirectory.Parent == null)
                {
                    throw new InvalidOperationException("请选择具体的源文件夹，不能将磁盘或共享根目录作为源文件夹。");
                }

                var finalDestPath = Path.TrimEndingDirectorySeparator(
                    Path.GetFullPath(Path.Combine(destPath, sourceDirectory.Name)));
                var sourceFullPath = Path.TrimEndingDirectorySeparator(sourceDirectory.FullName);
                if (string.Equals(finalDestPath, sourceFullPath, StringComparison.OrdinalIgnoreCase)
                    || finalDestPath.StartsWith(sourceFullPath + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase)
                    || sourceFullPath.StartsWith(finalDestPath + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("目标文件夹不能与源文件夹相同，也不能相互包含。");
                }

                if (!Directory.Exists(destPath))
                {
                    Directory.CreateDirectory(destPath);
                }

                if (Directory.Exists(finalDestPath))
                {
                    Directory.Delete(finalDestPath, true);
                }

                psi.FileName = "robocopy.exe";
                psi.ArgumentList.Add(sourcePath);
                psi.ArgumentList.Add(finalDestPath);
                psi.ArgumentList.Add("/e");
                psi.ArgumentList.Add("/move");
                psi.ArgumentList.Add("/copyall");
                psi.ArgumentList.Add("/r:3");
                psi.ArgumentList.Add("/w:3");
                psi.ArgumentList.Add("/mt:4");
                psi.ArgumentList.Add("/nfl");
                psi.ArgumentList.Add("/ndl");
                psi.ArgumentList.Add("/np");

                _logger.LogInformation("执行命令: robocopy \"{Source}\" \"{Destination}\" /move", sourcePath,
                    finalDestPath);

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
