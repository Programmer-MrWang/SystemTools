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
        if (string.IsNullOrWhiteSpace(Settings.AudioFilePath))
        {
            _logger.LogWarning("未设置音频文件路径，已跳过播放。");
            return;
        }

        if (!File.Exists(Settings.AudioFilePath))
        {
            _logger.LogWarning("音频文件不存在：{Path}", Settings.AudioFilePath);
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
                await PlayAudioFromFileAsync(audioService, Settings.AudioFilePath);
                _logger.LogInformation("音频播放完成：{Path}", Settings.AudioFilePath);
            }
            else
            {
                _ = PlayAudioFromFileAsync(audioService, Settings.AudioFilePath)
                    .ContinueWith(task =>
                    {
                        if (task.Exception != null)
                        {
                            _logger.LogError(task.Exception, "后台播放音频任务失败：{Path}", Settings.AudioFilePath);
                        }
                    }, TaskScheduler.Default);
                _logger.LogInformation("已拉起后台音频播放：{Path}", Settings.AudioFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "播放音频失败：{Path}", Settings.AudioFilePath);
            throw;
        }
    }

    private static async Task PlayAudioFromFileAsync(IAudioService audioService, string filePath)
    {
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await audioService.PlayAudioAsync(fs, 1.0f);
    }
}
