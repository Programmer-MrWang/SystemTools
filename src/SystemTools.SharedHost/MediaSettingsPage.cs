using Avalonia.Controls;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using SystemTools.Contracts;
using SystemTools.Shared;
using SystemTools.Services;
using ClassIsland.Shared;

namespace SystemTools;

[SettingsPageInfo("SystemTools.Media", "相机、语音与媒体")]
public sealed class MediaSettingsPage : SettingsPageBase
{
    public MediaSettingsPage(MediaPreferencesStore preferences, CameraCaptureContext camera, PlatformMediaServices media, PortableSpeechService speech)
    {
        var panel = new StackPanel { Spacing = 12, Margin = new Avalonia.Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "相机、语音与媒体", FontSize = 24 });
        if (GlobalConstants.Features?.RuntimeId == "osx-arm64")
            panel.Children.Add(new TextBlock { Text = "Mac 相机和麦克风由 SystemTools 采集助手访问。首次使用请在下方申请权限；也可以从菜单栏的采集助手停止采集。", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var ffmpeg = Field("FFmpeg 路径（留空时使用已安装的默认位置）", preferences.Current.FfmpegPath);
        var model = Field("离线语音模型文件夹", preferences.Current.SpeechModelPath);
        var microphone = Field("麦克风设备（default 为系统默认设备）", preferences.Current.MicrophoneDevice);
        var lyrics = Field("歌词文件（Linux：与当前曲目对应的 LRC 文件）", preferences.Current.LyricsFile);
        var authentication = new CheckBox { Content = "启用系统认证（保留已有 Windows Hello 设置）", IsChecked = preferences.Current.EnableSystemAuthentication };
        panel.Children.Add(authentication);
        CheckBox? aiEnabled = null;
        CheckBox? wakeEnabled = null;
        TextBox? endpoint = null;
        TextBox? aiModel = null;
        TextBox? sessionKey = null;
        if (GlobalConstants.Features?.RuntimeId != "win-x64")
        {
            aiEnabled = new() { Content = "启用 AI 对话", IsChecked = GlobalConstants.MainConfig!.Data.EnableAiService };
            wakeEnabled = new() { Content = "启用语音唤醒（会使用麦克风）", IsChecked = GlobalConstants.MainConfig.Data.EnableVoiceWakeAi };
            panel.Children.Add(aiEnabled); panel.Children.Add(wakeEnabled);
            endpoint = Field("AI 服务地址", GlobalConstants.MainConfig.Data.AiApiUrl);
            aiModel = Field("AI 模型名称", GlobalConstants.MainConfig.Data.AiModel);
            sessionKey = Field("本次应用运行的 API 密钥（仅内存，不保存）", "");
            sessionKey.PasswordChar = '●';
            Unloaded += (_, _) => sessionKey.Text = "";
            var credentialStatus = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            void CredentialStatus() => credentialStatus.Text = GlobalConstants.MainConfig!.Data.IsAiApiKeyUnavailable
                ? "原密钥当前无法读取，已保留原引用；请解锁系统凭据或显式重新保存。"
                : GlobalConstants.MainConfig.Data.SavedCredentialReference.Length > 0 ? "已保存系统凭据；留空不会覆盖。" : "尚未保存系统凭据，可仅使用本次会话密钥。";
            CredentialStatus(); panel.Children.Add(credentialStatus);
            var persist = new Button { Content = "将输入的密钥保存到系统凭据" };
            persist.Click += async (_, _) =>
            {
                persist.IsEnabled = false;
                try
                {
                    var value = sessionKey.Text ?? "";
                    await Task.Run(() => GlobalConstants.MainConfig!.Data.SaveApiKeySecurely(value));
                    sessionKey.Text = "";
                    if (IAppHost.TryGetService<IOpenAiCompatibleService>() is OpenAiCompatibleService client) client.SessionApiKey = null;
                    CredentialStatus();
                }
                catch (Exception error) when (error is System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException)
                { credentialStatus.Text = "保存未完成；原配置保留。请检查系统凭据服务或解锁后重试。"; }
                finally { persist.IsEnabled = true; }
            };
            var forget = new Button { Content = "移除当前已保存的密钥" };
            forget.Click += async (_, _) =>
            {
                forget.IsEnabled = false;
                try
                {
                    await Task.Run(() => GlobalConstants.MainConfig!.Data.ClearSavedApiKey());
                    sessionKey.Text = "";
                    if (IAppHost.TryGetService<IOpenAiCompatibleService>() is OpenAiCompatibleService client) client.SessionApiKey = null;
                    CredentialStatus();
                }
                catch (Exception error) when (error is System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException)
                { credentialStatus.Text = "移除未完成；请重新检查当前配置，解锁系统凭据后清理。"; }
                finally { forget.IsEnabled = true; }
            };
            panel.Children.Add(persist); panel.Children.Add(forget);
            var open = new Button { Content = "打开语音与文字对话" };
            open.Click += async (_, _) =>
            {
                if (IAppHost.TryGetService<NativeVoiceService>() is { } voice) await voice.OpenAsync();
            };
            panel.Children.Add(open);
        }
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var save = new Button { Content = "保存并重新检查" };
        save.Click += async (_, _) =>
        {
            try
            {
                preferences.Current.FfmpegPath = ffmpeg.Text?.Trim() ?? "";
                preferences.Current.SpeechModelPath = model.Text?.Trim() ?? "";
                preferences.Current.MicrophoneDevice = microphone.Text?.Trim() ?? "default";
                preferences.Current.EnableSystemAuthentication = authentication.IsChecked == true;
                preferences.Current.LyricsFile = lyrics.Text?.Trim() ?? "";
                preferences.Save();
                if (aiEnabled is not null)
                {
                    GlobalConstants.MainConfig!.Data.EnableAiService = aiEnabled.IsChecked == true;
                    GlobalConstants.MainConfig.Data.EnableVoiceWakeAi = wakeEnabled?.IsChecked == true;
                    GlobalConstants.MainConfig.Data.AiApiUrl = endpoint?.Text?.Trim() ?? "";
                    GlobalConstants.MainConfig.Data.AiModel = aiModel?.Text?.Trim() ?? "";
                    GlobalConstants.MainConfig.Save();
                    if (IAppHost.TryGetService<IOpenAiCompatibleService>() is OpenAiCompatibleService client)
                        client.SessionApiKey = sessionKey?.Text;
                }
                var value = camera.Camera is IAsyncCapabilityProbe probe ? await probe.ProbeAsync(camera.Stopping) : camera.Camera.Probe();
                speech.Input.RefreshBackground();
                status.Text = "已保存。摄像头：" + (value.CanExecute ? "采集后端就绪，请选择设备。" : value.Reason) + " 功能菜单变化后请重启应用。";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { status.Text = "设置未能保存，请检查文件权限或重新打开设置后重试。"; }
            catch (OperationCanceledException) { status.Text = "检查已取消。"; }
            catch (NativeFeatureException error) { status.Text = error.Message; }
        };
        panel.Children.Add(save);
        var list = new Button { Content = "检查摄像头设备" };
        list.Click += async (_, _) =>
        {
            list.IsEnabled = false;
            try
            {
                var devices = await camera.Camera.ListDevicesAsync(camera.Stopping);
                status.Text = devices.Count == 0 ? "没有可用摄像头。" : string.Join(Environment.NewLine, devices.Select(device => device.Name + "：" + device.Id));
            }
            catch (OperationCanceledException) { status.Text = "检查已取消。"; }
            catch (Exception error) { status.Text = error is NativeFeatureException ? error.Message : "设备检查未完成，请检查权限和依赖后重试。"; }
            finally { list.IsEnabled = true; }
        };
        panel.Children.Add(list);
        if (camera.Camera is IAsyncPermissionRequest permission)
        {
            var request = new Button { Content = "申请摄像头权限" };
            request.Click += async (_, _) =>
            {
                request.IsEnabled = false;
                try { status.Text = (await permission.RequestAccessAsync(camera.Stopping)).Reason; }
                catch (OperationCanceledException) { status.Text = "授权已取消。"; }
                catch (Exception error) { status.Text = error.Message; }
                finally { request.IsEnabled = true; }
            };
            panel.Children.Add(request);
        }
        panel.Children.Add(status);
        var dictation = new TextBox { AcceptsReturn = true, MinHeight = 90, PlaceholderText = "语音听写结果" };
        panel.Children.Add(dictation);
        var listen = new Button { Content = "开始听写" };
        var end = new Button { Content = "停止听写" };
        CancellationTokenSource? recording = null;
        listen.Click += async (_, _) =>
        {
            if (recording is not null) return;
            recording = CancellationTokenSource.CreateLinkedTokenSource(media.Stopping);
            listen.IsEnabled = false;
            try
            {
                await foreach (var segment in speech.Input.ListenAsync(recording.Token))
                {
                    if (segment.IsFinal) dictation.Text = (dictation.Text ?? "") + segment.Text + Environment.NewLine;
                    status.Text = segment.Text;
                }
            }
            catch (OperationCanceledException) { status.Text = "听写已停止。"; }
            catch (Exception error) { status.Text = error.Message; }
            finally { recording.Dispose(); recording = null; listen.IsEnabled = true; }
        };
        end.Click += (_, _) => recording?.Cancel();
        Unloaded += (_, _) => recording?.Cancel();
        panel.Children.Add(listen); panel.Children.Add(end);
        var authCheck = new Button { Content = "检查系统认证" };
        authCheck.Click += async (_, _) =>
        {
            try
            {
                var value = media.Authentication is IAsyncCapabilityProbe probe ? await probe.ProbeAsync(media.Stopping) : media.Authentication.Probe();
                status.Text = value.CanExecute ? "系统认证已就绪。" : value.Reason;
            }
            catch (OperationCanceledException) { status.Text = "检查已取消。"; }
        };
        panel.Children.Add(authCheck);
        if (media.Speech is IAsyncPermissionRequest microphonePermission)
        {
            var microphoneRequest = new Button { Content = "申请麦克风权限" };
            microphoneRequest.Click += async (_, _) =>
            {
                microphoneRequest.IsEnabled = false;
                try { status.Text = (await microphonePermission.RequestAccessAsync(media.Stopping)).Reason; }
                catch (OperationCanceledException) { status.Text = "授权已取消。"; }
                catch (Exception error) { status.Text = error.Message; }
                finally { microphoneRequest.IsEnabled = true; }
            };
            panel.Children.Add(microphoneRequest);
        }
        Content = new ScrollViewer { Content = panel };
        TextBox Field(string label, string value)
        {
            panel.Children.Add(new TextBlock { Text = label });
            var field = new TextBox { Text = value };
            panel.Children.Add(field);
            return field;
        }
    }
}
