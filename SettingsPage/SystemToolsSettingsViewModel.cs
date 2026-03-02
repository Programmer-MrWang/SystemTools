using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SystemTools.ConfigHandlers;
using System.Collections.ObjectModel;
using System;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using SystemTools.Shared;

namespace SystemTools;

public enum FeatureItemType
{
    Action,
    Trigger,
    Component
}

public partial class UnifiedFeatureItem : ObservableObject
{
    [ObservableProperty] private string _id = string.Empty;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private FeatureItemType _itemType;
    [ObservableProperty] private string? _groupName;

    public string TypeDisplayName => ItemType switch
    {
        FeatureItemType.Action => "行动",
        FeatureItemType.Trigger => "触发器",
        FeatureItemType.Component => "组件",
        _ => "未知"
    };
}

public partial class SystemToolsSettingsViewModel : ObservableObject
{
    [ObservableProperty] private MainConfigData _settings;

    // 两个独立的下载按钮启用状态
    [ObservableProperty] private bool _isFfmpegDownloadEnabled = true;
    [ObservableProperty] private bool _isFaceModelsDownloadEnabled = true;

    [ObservableProperty] private bool _showDownloadProgress = false;
    [ObservableProperty] private double _downloadProgress = 0;
    [ObservableProperty] private string _downloadStatusText = string.Empty;

    [ObservableProperty] private ObservableCollection<UnifiedFeatureItem> _featureItems = new();

    // Drawer 相关属性
    [ObservableProperty] private bool _isFeatureDrawerOpen = false;
    [ObservableProperty] private object? _featureDrawerContent;

    private readonly MainConfigHandler _configHandler;

    private const string DownloadUrl =
        "https://livefile.xesimg.com/programme/python_assets/f94fcfa40c9de41d6df09566a51e3130.exe";
    private const string ExpectedMd5 = "f94fcfa40c9de41d6df09566a51e3130";
    private const string TempFileName = "f94fcfa40c9de41d6df09566a51e3130.exe";
    private const string TargetFileName = "ffmpeg.exe";

    private const string FaceModelsUrl = "https://livefile.xesimg.com/programme/python_assets/915f822a03487c4e5761b4fcf8f206cc.zip";
    private const string FaceModelsMd5 = "915f822a03487c4e5761b4fcf8f206cc";
    private const string FaceZipFileName = "FaceModels.zip";

    public SystemToolsSettingsViewModel(MainConfigHandler configHandler)
    {
        _configHandler = configHandler;
        _settings = configHandler.Data;
    }

    public void InitializeFeatureItems()
    {
        FeatureItems.Clear();

        var components = new[]
        {
            ("SystemTools.NetworkStatus", "网络延迟"),
            ("SystemTools.LyricsDisplay", "歌词显示"),
        };
        foreach (var (id, name) in components)
        {
            FeatureItems.Add(new UnifiedFeatureItem
            {
                Id = id,
                DisplayName = name,
                IsEnabled = Settings.IsComponentEnabled(id),
                ItemType = FeatureItemType.Component,
                GroupName = null
            });
        }

        var triggers = new[]
        {
            ("SystemTools.UsbDeviceTrigger", "USB设备插入时"),
            ("SystemTools.HotkeyTrigger", "按下F9时"),
            ("SystemTools.ActionInProgressTrigger", "行动进行时"),
        };
        foreach (var (id, name) in triggers)
        {
            FeatureItems.Add(new UnifiedFeatureItem
            {
                Id = id,
                DisplayName = name,
                IsEnabled = Settings.IsTriggerEnabled(id),
                ItemType = FeatureItemType.Trigger,
                GroupName = null
            });
        }

        var actions = new[]
        {
            ("SystemTools.SimulateKeyboard", "模拟键盘", "模拟操作"),
            ("SystemTools.SimulateMouse", "模拟鼠标", "模拟操作"),
            ("SystemTools.TypeContent", "键入内容", "模拟操作"),
            ("SystemTools.WindowOperation", "窗口操作", "模拟操作"),
            ("SystemTools.AltF4", "按下 Alt+F4", "常用模拟键"),
            ("SystemTools.AltTab", "按下 Alt+Tab", "常用模拟键"),
            ("SystemTools.EnterKey", "按下 Enter 键", "常用模拟键"),
            ("SystemTools.EscKey", "按下 Esc 键", "常用模拟键"),
            ("SystemTools.F11Key", "按下 F11 键", "常用模拟键"),
            ("SystemTools.CloneDisplay", "复制屏幕", "显示设置"),
            ("SystemTools.ExtendDisplay", "扩展屏幕", "显示设置"),
            ("SystemTools.InternalDisplay", "仅电脑屏幕", "显示设置"),
            ("SystemTools.ExternalDisplay", "仅第二屏幕", "显示设置"),
            ("SystemTools.BlackScreenHtml", "黑屏html", "显示设置"),
            ("SystemTools.Shutdown", "计时关机", "电源选项"),
            ("SystemTools.CancelShutdown", "取消关机计划", "电源选项"),
            ("SystemTools.LockScreen", "锁定屏幕", "电源选项"),
            ("SystemTools.Copy", "复制", "文件操作"),
            ("SystemTools.Move", "移动", "文件操作"),
            ("SystemTools.Delete", "删除", "文件操作"),
            ("SystemTools.ChangeWallpaper", "切换壁纸", "系统个性化"),
            ("SystemTools.SwitchTheme", "切换主题色", "系统个性化"),
            ("SystemTools.FullscreenClock", "沉浸式时钟", "其他工具"),
            ("SystemTools.KillProcess", "退出进程", "实用工具"),
            ("SystemTools.ScreenShot", "屏幕截图", "实用工具"),
            ("SystemTools.SetVolume", "设置系统音量", "实用工具"),
            ("SystemTools.ShowToast", "拉起自定义Windows通知", "实用工具"),
            ("SystemTools.DisableDevice", "禁用硬件设备", "实用工具"),
            ("SystemTools.EnableDevice", "启用硬件设备", "实用工具"),
            ("SystemTools.TriggerCustomTrigger", "触发指定触发器", null),
            ("SystemTools.RestartAsAdmin", "重启应用为管理员身份", null),
        };

        foreach (var (id, name, group) in actions)
        {
            FeatureItems.Add(new UnifiedFeatureItem
            {
                Id = id,
                DisplayName = name,
                IsEnabled = Settings.IsActionEnabled(id),
                ItemType = FeatureItemType.Action,
                GroupName = group
            });
        }
    }

    public void SaveFeatureSettings()
    {
        foreach (var item in FeatureItems)
        {
            switch (item.ItemType)
            {
                case FeatureItemType.Action:
                    Settings.EnabledActions[item.Id] = item.IsEnabled;
                    break;
                case FeatureItemType.Trigger:
                    Settings.EnabledTriggers[item.Id] = item.IsEnabled;
                    break;
                case FeatureItemType.Component:
                    Settings.EnabledComponents[item.Id] = item.IsEnabled;
                    break;
            }
        }

        _configHandler.Save();
    }

    public bool CheckFfmpegExists()
    {
        try
        {
            var ffmpegPath = Path.Combine(
                GlobalConstants.Information.PluginFolder,
                TargetFileName);
            return File.Exists(ffmpegPath);
        }
        catch
        {
            return false;
        }
    }

    public bool CheckFaceModelsExists()
    {
        var modelDir = Path.Combine(GlobalConstants.Information.PluginFolder, "Models");
        return Directory.Exists(modelDir) &&
               File.Exists(Path.Combine(modelDir, "shape_predictor_68_face_landmarks.dat")) &&
               File.Exists(Path.Combine(modelDir, "dlib_face_recognition_resnet_model_v1.dat"));
    }

    public async Task<bool> DownloadFfmpegAsync(Func<Task> onError, Func<Task> onMd5Error)
    {
        if (!IsFfmpegDownloadEnabled) return false;

        IsFfmpegDownloadEnabled = false;
        ShowDownloadProgress = true;
        DownloadProgress = 0;
        DownloadStatusText = "正在下载 - 0%";

        var tempPath = Path.Combine(GlobalConstants.Information.PluginFolder, TempFileName);
        var targetPath = Path.Combine(GlobalConstants.Information.PluginFolder, TargetFileName);

        try
        {
            using var httpClient = new HttpClient();
            using var response = await httpClient.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var downloadedBytes = 0L;

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[4 * 1024 * 1024];
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                downloadedBytes += bytesRead;

                if (totalBytes > 0)
                {
                    var progress = (double)downloadedBytes / totalBytes * 100;
                    await UpdateProgressAsync(progress);
                }
            }

            fileStream.Close();
            await Task.Delay(500);
            await UpdateStatusAsync("正在校验MD5…");

            var actualMd5 = await CalculateMd5Async(tempPath);
            if (!string.Equals(actualMd5, ExpectedMd5, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(tempPath);
                await onMd5Error();
                return false;
            }

            await Task.Delay(500);
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            File.Move(tempPath, targetPath);
            await Task.Delay(500);
            ShowDownloadProgress = false;

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SystemTools] 下载失败: {ex.Message}");

            if (File.Exists(tempPath))
            {
                await Task.Delay(2000);
                File.Delete(tempPath);
            }

            await onError();
            return false;
        }
        finally
        {
            IsFfmpegDownloadEnabled = !CheckFfmpegExists();
            if (!ShowDownloadProgress)
            {
                DownloadProgress = 0;
                DownloadStatusText = string.Empty;
            }
        }
    }

    public async Task<bool> DownloadFaceModelsAsync(Func<Task> onError, Func<Task> onMd5Error)
    {
        if (!IsFaceModelsDownloadEnabled) return false;

        IsFaceModelsDownloadEnabled = false;
        ShowDownloadProgress = true;
        DownloadProgress = 0;

        var pluginFolder = GlobalConstants.Information.PluginFolder;
        var zipPath = Path.Combine(pluginFolder, FaceZipFileName);

        try
        {
            using var httpClient = new HttpClient();
            using var response = await httpClient.GetAsync(FaceModelsUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var downloadedBytes = 0L;

            await using (var contentStream = await response.Content.ReadAsStreamAsync())
            await using (var fileStream = new FileStream(zipPath, FileMode.Create))
            {
                var buffer = new byte[8192];
                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    downloadedBytes += bytesRead;
                    if (totalBytes > 0)
                    {
                        await UpdateProgressAsync((double)downloadedBytes / totalBytes * 100);
                    }
                }
            }

            await UpdateStatusAsync("正在校验模型 MD5…");
            var actualMd5 = await CalculateMd5Async(zipPath);
            if (!string.Equals(actualMd5, FaceModelsMd5, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(zipPath);
                await onMd5Error();
                return false;
            }

            await UpdateStatusAsync("正在解压模型文件…");
            await Task.Run(() =>
            {
                if (Directory.Exists(Path.Combine(pluginFolder, "temp_extract")))
                    Directory.Delete(Path.Combine(pluginFolder, "temp_extract"), true);

                ZipFile.ExtractToDirectory(zipPath, pluginFolder, true);
            });

            await UpdateStatusAsync("正在整理文件结构…");
            await Task.Run(() =>
            {
                string sourceDir = Path.Combine(pluginFolder, "新建文件夹");
                if (Directory.Exists(sourceDir))
                {
                    foreach (var dir in Directory.GetDirectories(sourceDir))
                    {
                        var dest = Path.Combine(pluginFolder, Path.GetFileName(dir));
                        if (Directory.Exists(dest)) Directory.Delete(dest, true);
                        Directory.Move(dir, dest);
                    }
                    foreach (var file in Directory.GetFiles(sourceDir))
                    {
                        var dest = Path.Combine(pluginFolder, Path.GetFileName(file));
                        if (File.Exists(dest)) File.Delete(dest);
                        File.Move(file, dest);
                    }
                    Directory.Delete(sourceDir, true);
                }
            });

            if (File.Exists(zipPath)) File.Delete(zipPath);

            await UpdateStatusAsync("处理完成！");
            await Task.Delay(1000);
            ShowDownloadProgress = false;
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SystemTools] 下载模型失败: {ex.Message}");
            if (File.Exists(zipPath)) File.Delete(zipPath);
            await onError();
            return false;
        }
        finally
        {
            IsFaceModelsDownloadEnabled = !CheckFaceModelsExists();
        }
    }

    private async Task UpdateProgressAsync(double progress)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            DownloadProgress = progress;
            DownloadStatusText = $"正在下载 - {progress:F0}%";
        });
    }

    private async Task UpdateStatusAsync(string status)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { DownloadStatusText = status; });
    }

    private static async Task<string> CalculateMd5Async(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await MD5.HashDataAsync(stream);
        return Convert.ToHexString(hash);
    }
}