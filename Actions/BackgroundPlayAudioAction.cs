using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;
using SystemTools.Settings;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.BackgroundPlayAudio", "后台播放音频", "\uE189", false)]
public class BackgroundPlayAudioAction(ILogger<BackgroundPlayAudioAction> logger) : ActionBase<BackgroundPlayAudioSettings>
{
    private readonly ILogger<BackgroundPlayAudioAction> _logger = logger;

    protected override async Task OnInvoke()
    {
        var normalizedPath = NormalizeAudioPath(Settings.AudioFilePath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            _logger.LogWarning("未设置音频文件路径，已跳过播放。");
            return;
        }

        if (!File.Exists(normalizedPath))
        {
            _logger.LogWarning("音频文件不存在：{Path}", normalizedPath);
            return;
        }

        if (ContainsChinese(normalizedPath))
        {
            _logger.LogWarning("后台播放音频不支持中文路径或中文文件名：{Path}", normalizedPath);
            return;
        }

        var audioService = IAppHost.TryGetService<IAudioService>();
        if (audioService == null)
        {
            _logger.LogWarning("未能获取 IAudioService，无法播放音频。");
            return;
        }

        try
        {
            if (Settings.WaitForPlaybackCompleted)
            {
                await PlayAudioFromFileAsync(audioService, normalizedPath);
                _logger.LogInformation("音频播放完成：{Path}", normalizedPath);
            }
            else
            {
                _ = PlayAudioFromFileAsync(audioService, normalizedPath)
                    .ContinueWith(task =>
                    {
                        if (task.Exception != null)
                        {
                            _logger.LogError(task.Exception, "后台播放音频任务失败：{Path}", normalizedPath);
                        }
                    }, TaskScheduler.Default);
                _logger.LogInformation("已拉起后台音频播放：{Path}", normalizedPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "播放音频失败：{Path}", normalizedPath);
            throw;
        }
    }

    private static async Task PlayAudioFromFileAsync(IAudioService audioService, string filePath)
    {
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await audioService.PlayAudioAsync(fs, 1.0f);
    }

    private static string NormalizeAudioPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = Uri.UnescapeDataString(path.Trim());
        if (OperatingSystem.IsWindows() && normalized.StartsWith("/") &&
            normalized.Length > 2 && char.IsLetter(normalized[1]) && normalized[2] == ':')
        {
            normalized = normalized[1..];
        }

        return normalized;
    }

    private static bool ContainsChinese(string text)
    {
        foreach (var c in text)
        {
            if (c is >= '\u4E00' and <= '\u9FFF')
            {
                return true;
            }
        }

        return false;
    }
}
