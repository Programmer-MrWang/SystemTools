using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace SystemTools.Helpers;

/// <summary>
/// 从壁纸图片中提取主色调，并换算为适合作为应用主题色的颜色。
/// </summary>
/// <remarks>
/// <para>
/// 只取壁纸的<b>色调</b>：先统计最占主导的色相，再按经验范围重新设定饱和度与亮度，
/// 使结果稳定落在适合充当主题色的区间内，避免出现发灰、发白或过于刺眼的情况。
/// </para>
/// <para>
/// 壁纸本身没有可用的色彩信息时（纯黑白灰图片）返回 <c>null</c>，由调用方决定是否保留原值。
/// </para>
/// </remarks>
internal static class WallpaperColorExtractor
{
    /// <summary>解码宽度。取色只需大致分布，小尺寸可显著降低耗时与内存占用。</summary>
    private const int SampleWidth = 160;

    private const double MinimumSaturation = 0.18;
    private const double MinimumValue = 0.12;
    private const double MaximumValue = 0.97;

    /// <summary>有效彩色像素占比低于此值时，认为壁纸没有可提取的色调。</summary>
    private const double MinimumColorfulPixelRatio = 0.005;

    private const int HueBucketCount = 72;

    // 目标区间：饱和度不至于灰，亮度适中，保证白字或深色文字都能压在上面。
    private const double TargetSaturationMinimum = 0.55;
    private const double TargetSaturationMaximum = 0.85;
    private const double TargetLightnessMinimum = 0.42;
    private const double TargetLightnessMaximum = 0.58;

    /// <summary>
    /// 从指定图片文件提取主题色。失败或图片无可提取色调时返回 <c>null</c>。
    /// </summary>
    public static Color? TryExtract(string imagePath)
    {
        try
        {
            using var stream = File.OpenRead(imagePath);
            return TryExtract(stream);
        }
        catch (Exception)
        {
            // 文件被占用、格式不受支持或路径失效时交由调用方尝试下一个候选来源。
            return null;
        }
    }

    /// <summary>
    /// 从图片数据流提取主题色。失败或图片无可提取色调时返回 <c>null</c>。
    /// </summary>
    public static Color? TryExtract(Stream stream)
    {
        try
        {
            using var bitmap = WriteableBitmap.DecodeToWidth(stream, SampleWidth, BitmapInterpolationMode.MediumQuality);
            using var framebuffer = bitmap.Lock();
            return ExtractFromFramebuffer(framebuffer);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Color? ExtractFromFramebuffer(ILockedFramebuffer framebuffer)
    {
        var width = framebuffer.Size.Width;
        var height = framebuffer.Size.Height;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var length = framebuffer.RowBytes * height;
        var pixels = new byte[length];
        Marshal.Copy(framebuffer.Address, pixels, 0, length);

        // 解码结果通常为 BGRA，个别平台为 RGBA，逐像素按格式取通道。
        var format = framebuffer.Format;
        var isBgra = format == PixelFormat.Bgra8888;
        var isRgba = format == PixelFormat.Rgba8888;
        if (!isBgra && !isRgba)
        {
            return null;
        }

        var bucketWeights = new double[HueBucketCount];
        var hueSin = new double[HueBucketCount];
        var hueCos = new double[HueBucketCount];
        var bucketSaturation = new double[HueBucketCount];
        var bucketLightness = new double[HueBucketCount];
        var bucketCounts = new int[HueBucketCount];

        long totalPixels = 0;
        long colorfulPixels = 0;
        var stride = framebuffer.RowBytes;

        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * stride;
            for (var x = 0; x < width; x++)
            {
                var offset = rowOffset + x * 4;
                if (offset + 3 >= pixels.Length)
                {
                    continue;
                }

                totalPixels++;

                var c0 = pixels[offset];
                var c1 = pixels[offset + 1];
                var c2 = pixels[offset + 2];
                byte r, g, b;
                if (isBgra)
                {
                    b = c0;
                    g = c1;
                    r = c2;
                }
                else
                {
                    r = c0;
                    g = c1;
                    b = c2;
                }

                var alpha = pixels[offset + 3];
                if (alpha < 32)
                {
                    // 透明区域不代表壁纸颜色，直接忽略。
                    continue;
                }

                RgbToHsl(r, g, b, out var hue, out var saturation, out var lightness);
                var value = Math.Max(r, Math.Max(g, b)) / 255.0;
                if (saturation < MinimumSaturation || value < MinimumValue || value > MaximumValue)
                {
                    // 黑白灰与过亮过暗的像素不体现色调，不参与统计。
                    continue;
                }

                colorfulPixels++;

                var bucket = (int)(hue / (360.0 / HueBucketCount)) % HueBucketCount;
                var weight = saturation;
                bucketWeights[bucket] += weight;
                bucketSaturation[bucket] += saturation * weight;
                bucketLightness[bucket] += lightness * weight;
                bucketCounts[bucket]++;
                var radians = hue * Math.PI / 180.0;
                hueSin[bucket] += Math.Sin(radians) * weight;
                hueCos[bucket] += Math.Cos(radians) * weight;
            }
        }

        if (totalPixels == 0 || colorfulPixels < totalPixels * MinimumColorfulPixelRatio)
        {
            return null;
        }

        var bestBucket = -1;
        var bestWeight = 0.0;
        for (var i = 0; i < HueBucketCount; i++)
        {
            if (bucketWeights[i] > bestWeight)
            {
                bestWeight = bucketWeights[i];
                bestBucket = i;
            }
        }

        if (bestBucket < 0 || bucketCounts[bestBucket] == 0)
        {
            return null;
        }

        // 相邻色相桶往往属于同一种主色，合并统计可避免主色被切碎后取值跳到一侧。
        var centerHue = CircularMeanHue(hueSin[bestBucket], hueCos[bestBucket]);
        var mergedWeight = bucketWeights[bestBucket];
        var mergedSaturation = bucketSaturation[bestBucket];
        var mergedLightness = bucketLightness[bestBucket];

        for (var i = 0; i < HueBucketCount; i++)
        {
            if (i == bestBucket || bucketCounts[i] == 0)
            {
                continue;
            }

            var candidateHue = CircularMeanHue(hueSin[i], hueCos[i]);
            if (HueDistance(candidateHue, centerHue) > 25)
            {
                continue;
            }

            mergedWeight += bucketWeights[i];
            mergedSaturation += bucketSaturation[i];
            mergedLightness += bucketLightness[i];
        }

        if (mergedWeight <= 0)
        {
            return null;
        }

        var averageSaturation = mergedSaturation / mergedWeight;
        var averageLightness = mergedLightness / mergedWeight;

        // 只保留色调：饱和度按原图的鲜艳程度适度提升并夹到可用区间；
        // 亮度向中段收敛，避免过亮或过暗导致主题色观感不佳。
        var targetSaturation = Math.Clamp(averageSaturation * 1.25, TargetSaturationMinimum, TargetSaturationMaximum);
        var targetLightness = Math.Clamp(averageLightness * 0.35 + 0.34, TargetLightnessMinimum, TargetLightnessMaximum);

        return HslToColor(centerHue, targetSaturation, targetLightness);
    }

    private static double CircularMeanHue(double sinSum, double cosSum)
    {
        var hue = Math.Atan2(sinSum, cosSum) * 180.0 / Math.PI;
        return hue < 0 ? hue + 360 : hue;
    }

    private static double HueDistance(double a, double b)
    {
        var difference = Math.Abs(a - b) % 360;
        return difference > 180 ? 360 - difference : difference;
    }

    private static void RgbToHsl(byte r, byte g, byte b, out double hue, out double saturation, out double lightness)
    {
        var rf = r / 255.0;
        var gf = g / 255.0;
        var bf = b / 255.0;
        var max = Math.Max(rf, Math.Max(gf, bf));
        var min = Math.Min(rf, Math.Min(gf, bf));
        var delta = max - min;
        lightness = (max + min) / 2.0;

        if (delta < 1e-9)
        {
            hue = 0;
            saturation = 0;
            return;
        }

        saturation = lightness > 0.5 ? delta / (2.0 - max - min) : delta / (max + min);

        if (max <= rf + 1e-9 && max >= rf - 1e-9)
        {
            hue = 60 * ((gf - bf) / delta);
        }
        else if (max <= gf + 1e-9 && max >= gf - 1e-9)
        {
            hue = 60 * ((bf - rf) / delta + 2);
        }
        else
        {
            hue = 60 * ((rf - gf) / delta + 4);
        }

        if (hue < 0)
        {
            hue += 360;
        }
    }

    private static Color HslToColor(double hue, double saturation, double lightness)
    {
        if (saturation < 1e-9)
        {
            var gray = ToByte(lightness);
            return Color.FromRgb(gray, gray, gray);
        }

        var c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var hp = hue / 60.0;
        var x = c * (1 - Math.Abs(hp % 2 - 1));
        var m = lightness - c / 2;

        double r1, g1, b1;
        switch ((int)Math.Floor(hp) % 6)
        {
            case 0: (r1, g1, b1) = (c, x, 0); break;
            case 1: (r1, g1, b1) = (x, c, 0); break;
            case 2: (r1, g1, b1) = (0, c, x); break;
            case 3: (r1, g1, b1) = (0, x, c); break;
            case 4: (r1, g1, b1) = (x, 0, c); break;
            default: (r1, g1, b1) = (c, 0, x); break;
        }

        return Color.FromRgb(ToByte(r1 + m), ToByte(g1 + m), ToByte(b1 + m));
    }

    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);
}
