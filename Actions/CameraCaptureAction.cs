using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using SystemTools.Settings;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.CameraCapture", "摄像头抓拍", "\uE39E", false)]
public class CameraCaptureAction(ILogger<CameraCaptureAction> logger) : ActionBase<CameraCaptureSettings>
{
    private readonly ILogger<CameraCaptureAction> _logger = logger;

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("CameraCaptureAction OnInvoke 开始");

        if (string.IsNullOrWhiteSpace(Settings.SaveFolder))
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
            if (!Directory.Exists(Settings.SaveFolder))
            {
                _logger.LogInformation("创建保存目录: {Dir}", Settings.SaveFolder);
                Directory.CreateDirectory(Settings.SaveFolder);
            }

            string fileName = $"摄像头抓拍{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.png";
            string fullPath = Path.Combine(Settings.SaveFolder, fileName);

            _logger.LogInformation("正在抓拍摄像头 '{Device}' 图像到: {Path}",
                Settings.DeviceName, fullPath);

            string? pluginDir = Path.GetDirectoryName(GetType().Assembly.Location);
            string ffmpegPath = Path.Combine(pluginDir ?? AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");

            if (!File.Exists(ffmpegPath))
            {
                throw new Exception($"找不到 ffmpeg.exe: {ffmpegPath}");
            }

            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-f dshow -i video=\"{Settings.DeviceName}\" -frames:v 1 -y \"{fullPath}\"",
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
                    _logger.LogInformation("摄像头抓拍成功: {FileName}", fileName);
                }
                else
                {
                    _logger.LogWarning("FFmpeg 失败，退出码: {ExitCode}, 错误: {Error}",
                        process.ExitCode, error);
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