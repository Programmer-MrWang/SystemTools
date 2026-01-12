/*using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using SystemTools.Rules;

namespace SystemTools.Rules;

[RuleInfo("SystemTools.ScreenColorRule", "屏幕顶部颜色", "\uE7C5")]
public class ScreenColorRule : RuleBase<ScreenColorRuleSettings>
{
    private readonly ILogger<ScreenColorRule> _logger;
    private const int TOP_PIXELS = 50;

    public ScreenColorRule(ILogger<ScreenColorRule> logger)
    {
        _logger = logger;
    }

    protected override bool IsMatched(ILayersService layersService, AutomationSettings automationSettings)
    {
        try
        {
            var screenWidth = Screen.PrimaryScreen.Bounds.Width;
            var pixelCount = screenWidth * TOP_PIXELS;

            using var bitmap = new Bitmap(screenWidth, TOP_PIXELS);
            using var g = Graphics.FromImage(bitmap);
            g.CopyFromScreen(0, 0, 0, 0, new Size(screenWidth, TOP_PIXELS));

            long totalR = 0, totalG = 0, totalB = 0;

            var rect = new Rectangle(0, 0, screenWidth, TOP_PIXELS);
            var bitmapData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try
            {
                var ptr = bitmapData.Scan0;
                var bytes = Math.Abs(bitmapData.Stride) * TOP_PIXELS;
                var rgbValues = new byte[bytes];

                Marshal.Copy(ptr, rgbValues, 0, bytes);

                for (var i = 0; i < rgbValues.Length; i += 4)
                {
                    totalB += rgbValues[i];
                    totalG += rgbValues[i + 1];
                    totalR += rgbValues[i + 2];
                }
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }

            var avgBrightness = (totalR + totalG + totalB) / (3.0 * pixelCount);

            _logger.LogDebug("屏幕顶部平均亮度: {Brightness}, 判断模式: {Mode}",
                avgBrightness, Settings.Mode);

            var isBright = avgBrightness > 128;

            return Settings.Mode == "偏白" ? isBright : !isBright;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检查屏幕顶部颜色失败");
            return false;
        }
    }
}*/