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

[ActionInfo("SystemTools.Move", "移动", "\uE6E7", false)]
public class MoveAction(ILogger<MoveAction> logger, IProcessRunner processRunner) : ActionBase<MoveSettings>
{
    private readonly ILogger<MoveAction> _logger = logger;
    private readonly IProcessRunner _processRunner = processRunner;

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("MoveAction OnInvoke 开始");

        if (Settings == null || string.IsNullOrWhiteSpace(Settings.SourcePath) ||
            string.IsNullOrWhiteSpace(Settings.DestinationPath))
        {
            _logger.LogWarning("路径为空");
            return;
        }

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

                await Task.Run(() => File.Move(sourcePath, destPath));
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

                var psi = new ProcessStartInfo
                {
                    FileName = "robocopy.exe",
                    Arguments = $"\"{sourcePath}\" \"{finalDestPath}\" /e /move /copyall /r:3 /w:3 /mt:4 /nfl /ndl /np",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                await _processRunner.RunAsync(
                    psi,
                    operationName: "移动文件夹(robocopy)",
                    successExitCodes: new[] { 0, 1, 2, 3, 4, 5, 6, 7 },
                    timeout: TimeSpan.FromMinutes(10));

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
