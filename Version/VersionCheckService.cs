using ClassIsland.Platforms.Abstraction;
using ClassIsland.Platforms.Abstraction.Models;
using ClassIsland.Shared;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using SystemTools.Shared;

namespace SystemTools.Services;

public static class VersionCheckService
{
    private const string VersionFileName = "version.json";

    public static void CheckAndNotify()
    {
        var versionFilePath = Path.Combine(
            GlobalConstants.Information.PluginFolder,
            VersionFileName);

        var currentVersion = GlobalConstants.Information.PluginVersion;

        if (!File.Exists(versionFilePath))
        {
            CreateVersionFile(versionFilePath, currentVersion);
            ShowUpdateNotification(currentVersion, isFirstInstall: true);
            return;
        }

        try
        {
            var json = File.ReadAllText(versionFilePath);
            var savedVersion = JsonSerializer.Deserialize<VersionInfo>(json)?.Version;

            if (savedVersion == currentVersion)
            {
                return;
            }

            CreateVersionFile(versionFilePath, currentVersion);
            ShowUpdateNotification(currentVersion, isFirstInstall: false);
        }
        catch
        {
            CreateVersionFile(versionFilePath, currentVersion);
            ShowUpdateNotification(currentVersion, isFirstInstall: true);
        }
    }

    private static void CreateVersionFile(string path, string version)
    {
        try
        {
            var versionInfo = new VersionInfo { Version = version, LastCheckDate = DateTime.Now };
            var json = JsonSerializer.Serialize(versionInfo, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SystemTools] 创建版本文件失败: {ex.Message}");
        }
    }

    private static void ShowUpdateNotification(string version, bool isFirstInstall)
    {
        var title = "SystemTools";
        var message = isFirstInstall
            ? $"插件 SystemTools 已安装，版本 {version}，点击查看使用方法和帮助。"
            : $"插件 SystemTools 已更新至版本 {version}，点击查看更新日志。";

        Task.Delay(3000).ContinueWith(_ =>
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await PlatformServices.DesktopToastService.ShowToastAsync(
                    title,
                    message,
                    () => { NavigateToChangelog(); }
                );
            });
        });
    }

    private static void NavigateToChangelog()
    {
        try
        {
            GlobalConstants.ShowChangelogOnOpen = true;

            var uri = new Uri("classisland://app/settings/systemtools.settings.about");
            IAppHost.TryGetService<ClassIsland.Core.Abstractions.Services.IUriNavigationService>()
                ?.NavigateWrapped(uri);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SystemTools] 导航失败: {ex.Message}");
        }
    }

    private class VersionInfo
    {
        public string Version { get; set; } = string.Empty;
        public DateTime LastCheckDate { get; set; }
    }
}