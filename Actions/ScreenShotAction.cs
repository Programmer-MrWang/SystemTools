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

[ActionInfo("SystemTools.ScreenShot", "屏幕截图", "\uEEE7", false)]
public class ScreenShotAction(ILogger<ScreenShotAction> logger) : ActionBase<ScreenShotSettings>
{
    private readonly ILogger<ScreenShotAction> _logger = logger;

    protected override async Task OnInvoke()
    {
        _logger.LogDebug("ScreenShotAction OnInvoke 开始");

        if (string.IsNullOrWhiteSpace(Settings.SaveFolder))
        {
            _logger.LogWarning("保存路径为空");
            return;
        }

        try
        {
            if (!Directory.Exists(Settings.SaveFolder))
            {
                _logger.LogInformation("创建保存目录: {Dir}", Settings.SaveFolder);
                Directory.CreateDirectory(Settings.SaveFolder);
            }

            string fileName = $"屏幕截图{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.png";
            string fullPath = Path.Combine(Settings.SaveFolder, fileName);

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
                bitmap.Save(fullPath, ImageFormat.Png);
            }

            _logger.LogInformation("屏幕截图已保存到: {FileName}", fileName);
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