using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel;
using System.Diagnostics;
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

        if (Settings == null || string.IsNullOrWhiteSpace(Settings.ImagePath))
        {
            _logger.LogWarning("图片路径为空");
            return;
        }

        if (!File.Exists(Settings.ImagePath))
        {
            _logger.LogError("图片文件不存在: {Path}", Settings.ImagePath);
            throw new FileNotFoundException("指定的图片文件不存在", Settings.ImagePath);
        }

        try
        {
            var imagePath = Settings.ImagePath;
            _logger.LogInformation("正在切换壁纸到: {Path}", imagePath);
            IntPtr uniPtr = Marshal.StringToHGlobalUni(imagePath);
            bool result;
            unsafe
            {
                void* uniVoidPtr = (void*)uniPtr;
                result=PInvoke.SystemParametersInfo(Windows.Win32.UI.WindowsAndMessaging.SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETDESKWALLPAPER, 0, uniVoidPtr, Windows.Win32.UI.WindowsAndMessaging.SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS.SPIF_UPDATEINIFILE | Windows.Win32.UI.WindowsAndMessaging.SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS.SPIF_SENDCHANGE);
            }
            Marshal.FreeHGlobal(uniPtr);
            if (!result) throw new Win32Exception(Marshal.GetLastWin32Error(),"SystemParametersInfo失败");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "切换壁纸失败");
            throw;
        }

        await base.OnInvoke();
        _logger.LogDebug("ChangeWallpaperAction OnInvoke 完成");
    }
}
