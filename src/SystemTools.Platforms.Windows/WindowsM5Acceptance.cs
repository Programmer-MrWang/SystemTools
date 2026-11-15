using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Shared.Models.Automation;
using Microsoft.Extensions.DependencyInjection;
using SystemTools.Contracts;
using SystemTools.Services;
using SystemTools.Settings;
using SystemTools.Shared;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace SystemTools.Platforms.Windows;

internal sealed record WindowsM5Result(bool Passed, IReadOnlyList<string> Checks, string? Error, object? Details);

internal static class WindowsM5Acceptance
{
    internal static async Task<WindowsM5Result> RunAsync(IServiceProvider provider)
    {
        var checks = new List<string>();
        var details = new Dictionary<string, object?>();
        WindowsM5Result result;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(70));
        var token = deadline.Token;
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
        try
        {
            var media = provider.GetRequiredService<PlatformMediaServices>();
            var camera = Environment.GetEnvironmentVariable("SystemTools_M5_Camera") ?? throw new InvalidOperationException("Missing explicit camera fixture.");
            var wave = Environment.GetEnvironmentVariable("SystemTools_M5_TestWave") ?? throw new InvalidOperationException("Missing explicit speech fixture.");
            var folder = Path.Combine(GlobalConstants.PluginConfigFolder!, "M5-camera");
            var action = provider.GetRequiredKeyedService<ActionBase>("SystemTools.CameraCapture");
            var item = new ActionItem { Id = "SystemTools.CameraCapture", Settings = new CameraCaptureSettings { DeviceName = camera, SaveFolder = folder } };
            var set = new ActionSet(); set.ActionItems.Add(item); set.SetStartRunning(true);
            try { await action.InvokeAsync(item, set, isRevertable: false); }
            finally { set.SetEndRunning(true); }
            var image = Directory.EnumerateFiles(folder, "*.png").Single();
            using (var bitmap = new Bitmap(image)) Check(bitmap.PixelSize.Width > 0 && bitmap.PixelSize.Height > 0 && item.Exception is null,
                "真实宿主相机行动生成可解码 PNG");
            details["cameraImage"] = image;
            var speech = provider.GetRequiredService<VoskSpeechService>();
            var keywords = provider.GetRequiredService<KeywordSpeechService>();
            var speechErrors = new List<string>();
            var frames = 0;
            using (keywords.SuspendListening())
            using (var model = await speech.TryAcquireModelAsync(message => { lock (speechErrors) speechErrors.Add(message); }, token))
            {
                Check(model is not null && speech.IsModelLoaded, "Windows Worker 实际加载显式选择的本地模型");
                using (var capture = await speech.TryStartCaptureAsync((_, _) => { },
                    message => { lock (speechErrors) speechErrors.Add(message); }, _ => Interlocked.Increment(ref frames), token))
                {
                    Check(capture is not null && speech.IsDictationActive, "实际打开系统默认麦克风");
                    await Task.Delay(1500, token);
                    Check(Volatile.Read(ref frames) > 0, "Worker 收到真实麦克风 PCM 并报告音量");
                }
                await WaitAsync(() => !speech.IsDictationActive, token);
                Check(!speech.IsDictationActive, "结束录音后 Worker 确认停止并释放采集");
                using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                var rejected = await speech.TryStartCaptureAsync((_, _) => { }, _ => { }, cancelled.Token);
                rejected?.Dispose();
                Check(rejected is null && !speech.IsDictationActive, "预取消不会再次打开麦克风");
            }
            Check(speechErrors.Count == 0, "模型与麦克风会话没有后台错误");
            details["audioLevelMessages"] = frames;
            if (media.Authentication is WindowsSystemAuthentication authentication)
            {
                var support = await authentication.ProbeAsync(token);
                details["windowsHello"] = new { state = support.State.ToString(), support.Reason, interactiveVerificationPerformed = false };
                Check(!string.IsNullOrWhiteSpace(support.Reason), "实际读取本机 Windows Hello 配置状态");
                var invalidParent = await authentication.VerifyAsync("M5 isolated test", 0, token);
                Check(!invalidParent.Verified, "缺少认证窗口时不放行且不弹系统认证");
            }
            using (var player = new MediaPlayer { Volume = 0, IsLoopingEnabled = true })
            {
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                player.MediaOpened += (_, _) => ready.TrySetResult();
                player.MediaFailed += (_, args) => ready.TrySetException(new IOException(args.ErrorMessage));
                player.Source = MediaSource.CreateFromUri(new Uri(Path.GetFullPath(wave)));
                player.Play();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
                var controls = player.SystemMediaTransportControls;
                controls.IsEnabled = true; controls.IsPlayEnabled = true; controls.IsPauseEnabled = true;
                controls.DisplayUpdater.Type = MediaPlaybackType.Music;
                controls.DisplayUpdater.MusicProperties.Title = "SystemTools M5 Fixture";
                controls.DisplayUpdater.Update(); controls.PlaybackStatus = MediaPlaybackStatus.Playing;
                await WaitMediaAsync(media.Media, "Playing", token);
                Check(true, "真实系统媒体 API 发现隔离合成播放会话");
                player.Pause(); controls.PlaybackStatus = MediaPlaybackStatus.Paused;
                await WaitMediaAsync(media.Media, "Paused", token);
                Check(true, "真实媒体会话播放到暂停的状态迁移");
            }
            await WindowsM5ConversationAcceptance.RunAsync(provider, Check, token);
            result = new(true, checks, null, details);
        }
        catch (Exception error) { result = new(false, checks, error.ToString(), details); }
        File.WriteAllText(Path.Combine(GlobalConstants.PluginConfigFolder!, "M5.Acceptance.json"), JsonSerializer.Serialize(result,
            new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        return result;
    }

    private static async Task WaitAsync(Func<bool> condition, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(6));
        while (!condition()) await Task.Delay(50, timeout.Token);
    }
    private static async Task WaitMediaAsync(IMediaSessions source, string state, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(8));
        while (!(await source.ReadAsync(timeout.Token)).Any(session => session.Title == "SystemTools M5 Fixture" && session.State == state))
            await Task.Delay(150, timeout.Token);
    }
}
