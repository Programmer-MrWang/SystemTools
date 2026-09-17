using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace SystemTools.Helpers;

/// <summary>
/// 定位当前桌面壁纸对应的图片文件。
/// </summary>
/// <remarks>
/// 按可靠性从高到低枚举候选路径，调用方按顺序尝试解码，取第一个成功的结果：
/// <list type="number">
/// <item><c>%APPDATA%\Microsoft\Windows\Themes\TranscodedWallpaper</c>：Windows 实际用于绘制的图片，
/// 幻灯片播放时会随每次切换更新，因此静态壁纸与幻灯片都能取到当前画面；</item>
/// <item><c>HKCU\Control Panel\Desktop\WallPaper</c>：经典接口记录的壁纸路径；</item>
/// <item><c>HKCU\...\Explorer\Wallpapers\BackgroundHistoryPath0</c>：资源管理器记录的最近一张壁纸。</item>
/// </list>
/// 动态壁纸（如 Wallpaper Engine）不会更新上述任何一项，此时取到的仍是上一张静态壁纸。
/// </remarks>
internal static class DesktopWallpaperLocator
{
    private const string ThemesRelativePath = @"Microsoft\Windows\Themes\TranscodedWallpaper";
    private const string DesktopRegistryPath = @"Control Panel\Desktop";
    private const string WallpaperHistoryRegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers";

    /// <summary>
    /// 按优先级枚举可能存在的壁纸图片路径。仅返回确实存在的文件。
    /// </summary>
    public static IEnumerable<string> GetCandidatePaths()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in EnumerateCandidates())
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            string expanded;
            try
            {
                expanded = Environment.ExpandEnvironmentVariables(candidate);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (!seen.Add(expanded) || !File.Exists(expanded))
            {
                continue;
            }

            yield return expanded;
        }
    }

    private static IEnumerable<string?> EnumerateCandidates()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData))
        {
            yield return Path.Combine(appData, ThemesRelativePath);
        }

        yield return ReadRegistryString(DesktopRegistryPath, "WallPaper");
        yield return ReadRegistryString(WallpaperHistoryRegistryPath, "BackgroundHistoryPath0");
    }

    private static string? ReadRegistryString(string path, string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(path);
            return key?.GetValue(name) as string;
        }
        catch (Exception)
        {
            // 注册表项不存在或无权访问时静默跳过该候选来源。
            return null;
        }
    }
}
