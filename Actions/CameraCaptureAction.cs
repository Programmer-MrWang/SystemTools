using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using SystemTools.Settings;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.CameraCapture", "摄像头抓拍", "\uE39E",false)]
public class CameraCaptureAction(ILogger<CameraCaptureAction> logger) : ActionBase<CameraCaptureSettings>
{
    private readonly ILogger<CameraCaptureAction> _logger = logger;

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("CameraCaptureAction OnInvoke 开始");

        if (string.IsNullOrWhiteSpace(Settings.SavePath))
        {
            _logger.LogWarning("保存路径为空");
            throw new Exception("保存路径不能为空");
        }

        if (string.IsNullOrWhiteSpace(Settings.DeviceName))
        {
            _logger.LogWarning("设备名为空");
            throw new Exception("摄像头设备名不能为空");
        }

        try
        {
            string outputDir = Path.GetDirectoryName(Settings.SavePath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                _logger.LogInformation("创建输出目录: {Dir}", outputDir);
                Directory.CreateDirectory(outputDir);
            }

            if (File.Exists(Settings.SavePath))
            {
                _logger.LogDebug("删除已存在的文件: {Path}", Settings.SavePath);
                try { File.Delete(Settings.SavePath); }
                catch (Exception ex) { _logger.LogWarning(ex, "删除旧文件失败"); }
            }

            string pluginDir = Path.GetDirectoryName(GetType().Assembly.Location);
            string ffmpegPath = Path.Combine(pluginDir ?? AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");

            if (!File.Exists(ffmpegPath))
            {
                throw new Exception($"找不到 ffmpeg.exe: {ffmpegPath}");
            }

            _logger.LogInformation("正在抓拍摄像头 '{Device}' 图像到: {Path}",
                Settings.DeviceName, Settings.SavePath);

            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-f dshow -i video=\"{Settings.DeviceName}\" -frames:v 1 -y \"{Settings.SavePath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    _logger.LogInformation("摄像头抓拍成功");
                }
                else
                {
                    _logger.LogWarning("FFmpeg 失败，退出码: {ExitCode}, 输出: {Output}, 错误: {Error}",
                        process.ExitCode, output, error);
                    throw new Exception($"摄像头抓拍失败: {error}");
                }
            }
            else
            {
                throw new Exception("无法启动 FFmpeg 进程");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "摄像头抓拍失败");
            throw;
        }

        await base.OnInvoke();
        _logger.LogDebug("CameraCaptureAction OnInvoke 完成");
    }
}