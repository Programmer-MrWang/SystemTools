using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using SystemTools.Settings;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.ScreenShot", "屏幕截图", "\uEEE7",false)]
public class ScreenShotAction(ILogger<ScreenShotAction> logger) : ActionBase<ScreenShotSettings>
{
    private readonly ILogger<ScreenShotAction> _logger = logger;

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("ScreenShotAction OnInvoke 开始");

        if (string.IsNullOrWhiteSpace(Settings.SavePath))
        {
            _logger.LogWarning("保存路径为空");
            return;
        }

        try
        {
            string path = Settings.SavePath;
            string? directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var screen = Screen.PrimaryScreen;
            if (screen == null)
            {
                _logger.LogError("无法获取主显示器");
                return;
            }

            using (Bitmap bitmap = new(screen.Bounds.Width, screen.Bounds.Height))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(screen.Bounds.X, screen.Bounds.Y, 0, 0, screen.Bounds.Size);
                bitmap.Save(path, ImageFormat.Png);
            }

            _logger.LogInformation("屏幕截图已保存到: {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "屏幕截图失败");
            throw;
        }

        await base.OnInvoke();
        _logger.LogDebug("ScreenShotAction OnInvoke 完成");
    }
}