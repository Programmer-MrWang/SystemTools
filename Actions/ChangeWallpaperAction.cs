using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SystemTools.Settings;
using Windows.Win32;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.ChangeWallpaper", "切换壁纸", "\uE9BC", false)]
public class ChangeWallpaperAction(ILogger<ChangeWallpaperAction> logger) : ActionBase<ChangeWallpaperSettings>
{
    private readonly ILogger<ChangeWallpaperAction> _logger = logger;

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("ChangeWallpaperAction OnInvoke 开始");

        if (Settings == null)
        {
            _logger.LogWarning("壁纸设置为空");
            return;
        }

        if (Settings.Mode == ChangeWallpaperMode.Image && string.IsNullOrWhiteSpace(Settings.ImagePath))
        {
            _logger.LogWarning("图片路径为空");
            return;
        }

        if (Settings.Mode == ChangeWallpaperMode.Image && !File.Exists(Settings.ImagePath))
        {
            _logger.LogError("图片文件不存在: {Path}", Settings.ImagePath);
            throw new FileNotFoundException("指定的图片文件不存在", Settings.ImagePath);
        }

        try
        {
            if (Settings.Mode == ChangeWallpaperMode.SolidColor)
            {
                var color = ParseColor(Settings.SolidColor);
                _logger.LogInformation("正在切换为纯色壁纸: {Color}", Settings.SolidColor);
                await Task.Run(() => SetSolidColorWallpaper(color));
                _logger.LogInformation("切换纯色壁纸成功: {Color}", Settings.SolidColor);
            }
            else
            {
                var imagePath = Settings.ImagePath;
                var fit = Settings.FitStyle;
                _logger.LogInformation("正在切换壁纸到: {Path}, FitStyle: {Fit}", imagePath, fit);

                var (tileValue, styleValue) = fit switch
                {
                    0 => ("1", "1"),    // 平铺
                    1 => ("0", "0"),    // 居中
                    2 => ("0", "2"),    // 拉伸
                    3 => ("0", "10"),   // 填充
                    4 => ("0", "6"),    // 适应
                    5 => ("0", "22"),   // 跨区
                    _ => ("0", "10")    // 默认：填充
                };

                await Task.Run(() => SetImageWallpaper(imagePath, tileValue, styleValue));
                _logger.LogInformation("切换壁纸成功: {Path}", imagePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "切换壁纸失败");
            // 保持向上抛出异常，让上层 UI/宿主决定如何反馈给用户
            throw;
        }

        await base.OnInvoke();
        _logger.LogDebug("ChangeWallpaperAction OnInvoke 完成");
    }

    private static (byte R, byte G, byte B) ParseColor(string value)
    {
        var color = value.Trim();
        if (color.StartsWith("#"))
        {
            color = color[1..];
        }

        if (color.Length == 6 && int.TryParse(color, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
        {
            return ((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
        }

        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 3 &&
            byte.TryParse(parts[0], out var r) &&
            byte.TryParse(parts[1], out var g) &&
            byte.TryParse(parts[2], out var b))
        {
            return (r, g, b);
        }

        throw new FormatException("纯色壁纸颜色格式无效，请使用 #RRGGBB 或 R,G,B。");
    }

    private static void SetImageWallpaper(string imagePath, string tileValue, string styleValue)
    {
        using (var desktopRegKey = Registry.CurrentUser.OpenSubKey("Control Panel\\Desktop", true))
        {
            if (desktopRegKey == null)
            {
                throw new Win32Exception("无法访问系统桌面注册表配置项，操作失败。");
            }

            desktopRegKey.SetValue("TileWallpaper", tileValue, RegistryValueKind.String);
            desktopRegKey.SetValue("WallpaperStyle", styleValue, RegistryValueKind.String);
        }

        ApplyWallpaper(imagePath);
    }

    private static void SetSolidColorWallpaper((byte R, byte G, byte B) color)
    {
        using (var colorsRegKey = Registry.CurrentUser.OpenSubKey("Control Panel\\Colors", true))
        {
            if (colorsRegKey == null)
            {
                throw new Win32Exception("无法访问系统颜色注册表配置项，操作失败。");
            }

            colorsRegKey.SetValue("Background", $"{color.R} {color.G} {color.B}", RegistryValueKind.String);
        }

        using (var desktopRegKey = Registry.CurrentUser.OpenSubKey("Control Panel\\Desktop", true))
        {
            if (desktopRegKey == null)
            {
                throw new Win32Exception("无法访问系统桌面注册表配置项，操作失败。");
            }

            desktopRegKey.SetValue("Wallpaper", string.Empty, RegistryValueKind.String);
            desktopRegKey.SetValue("TileWallpaper", "0", RegistryValueKind.String);
            desktopRegKey.SetValue("WallpaperStyle", "0", RegistryValueKind.String);
        }

        ApplyWallpaper(string.Empty);
    }

    private static void ApplyWallpaper(string wallpaperPath)
    {
        IntPtr uniPtr = IntPtr.Zero;
        try
        {
            uniPtr = Marshal.StringToHGlobalUni(wallpaperPath);
            bool result;
            unsafe
            {
                result = PInvoke.SystemParametersInfo(
                    Windows.Win32.UI.WindowsAndMessaging.SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETDESKWALLPAPER,
                    0,
                    (void*)uniPtr,
                    Windows.Win32.UI.WindowsAndMessaging.SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS.SPIF_UPDATEINIFILE |
                    Windows.Win32.UI.WindowsAndMessaging.SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS.SPIF_SENDCHANGE);
            }

            if (!result)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SystemParametersInfo失败");
            }
        }
        finally
        {
            if (uniPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(uniPtr);
            }
        }
    }
}
