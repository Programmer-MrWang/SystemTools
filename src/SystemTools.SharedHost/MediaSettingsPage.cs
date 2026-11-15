using Avalonia.Controls;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using SystemTools.Contracts;

namespace SystemTools;

[SettingsPageInfo("SystemTools.Media", "相机、语音与媒体")]
public sealed class MediaSettingsPage : SettingsPageBase
{
    public MediaSettingsPage(MediaPreferencesStore preferences, CameraCaptureContext camera)
    {
        var panel = new StackPanel { Spacing = 12, Margin = new Avalonia.Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "相机、语音与媒体", FontSize = 24 });
        var ffmpeg = Field("FFmpeg 路径（留空时使用已安装的默认位置）", preferences.Current.FfmpegPath);
        var model = Field("离线语音模型文件夹", preferences.Current.SpeechModelPath);
        var microphone = Field("麦克风设备（default 为系统默认设备）", preferences.Current.MicrophoneDevice);
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var save = new Button { Content = "保存并重新检查" };
        save.Click += (_, _) =>
        {
            try
            {
                preferences.Current.FfmpegPath = ffmpeg.Text?.Trim() ?? "";
                preferences.Current.SpeechModelPath = model.Text?.Trim() ?? "";
                preferences.Current.MicrophoneDevice = microphone.Text?.Trim() ?? "default";
                preferences.Save();
                var value = camera.Camera.Probe();
                status.Text = "已保存。摄像头：" + (value.CanExecute ? "采集后端就绪，请选择设备。" : value.Reason) + " 功能菜单变化后请重启应用。";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { status.Text = "设置未能保存，请检查文件权限或重新打开设置后重试。"; }
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
                finally { request.IsEnabled = true; }
            };
            panel.Children.Add(request);
        }
        panel.Children.Add(status);
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
