using SkiaSharp;

namespace SystemTools;

/// <summary>Uses the host's existing Skia codec; dimensions are checked before allocating decoded pixels.</summary>
public static class NativeRasterImage
{
    public static SKBitmap Read(Stream stream, long maximumPixels)
    {
        using var codec = SKCodec.Create(stream) ?? throw new IOException("无法识别图片格式。");
        if (codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > maximumPixels)
            throw new IOException("图片像素过大，请缩小后重试。");
        return SKBitmap.Decode(codec) ?? throw new IOException("图片解码未完成。");
    }
    public static void WriteSolid(string path, int width, int height, byte red, byte green, byte blue)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(new SKColor(red, green, blue));
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100) ?? throw new IOException("无法编码图片。");
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        png.SaveTo(output);
    }
}
