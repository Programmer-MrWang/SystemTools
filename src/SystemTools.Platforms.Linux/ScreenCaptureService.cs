using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SystemTools.Contracts;

namespace SystemTools.Platforms.Linux;

public sealed class ScreenCaptureService : IScreenCapture
{
    public Capability Probe()
    {
        try { using var connection = new X11Connection(); return new(CapabilityState.Available); }
        catch (Exception error) when (error is DllNotFoundException or NativeFeatureException) { return new(CapabilityState.MissingDependency, "请检查原生 X11 显示服务。"); }
    }
    public Task CapturePngAsync(string destination, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        using var connection = new X11Connection();
        var root = X11Connection.Root(connection.Display);
        if (Geometry(connection.Display, root, out _, out _, out _, out var width, out var height, out _, out _) == 0 || width == 0 || height == 0)
            throw new NativeFeatureException(CapabilityState.MissingDevice, "无法获取显示器尺寸。");
        var originX = 0;
        var originY = 0;
        var monitors = GetMonitors(connection.Display, root, 1, out var count);
        if (monitors == 0 || count == 0) throw new NativeFeatureException(CapabilityState.MissingDevice, "无法确定主显示器，请检查 XRandR。");
        try
        {
            var all = Enumerable.Range(0, count).Select(index => Marshal.PtrToStructure<Monitor>(monitors + index * Marshal.SizeOf<Monitor>())).ToArray();
            var primary = all.FirstOrDefault(item => item.Primary != 0);
            if (primary.Width == 0) primary = all[0];
            originX = primary.X; originY = primary.Y;
            width = checked((uint)primary.Width); height = checked((uint)primary.Height);
        }
        finally { FreeMonitors(monitors); }
        var image = GetImage(connection.Display, root, originX, originY, width, height, nuint.MaxValue, 2);
        if (image == 0) throw new IOException("无法采集 X11 图像。");
        try
        {
            using var bitmap = new WriteableBitmap(new PixelSize(checked((int)width), checked((int)height)), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (var target = bitmap.Lock())
            {
                var row = new byte[checked((int)width * 4)];
                var layout = Marshal.PtrToStructure<ImageLayout>(image);
                for (var y = 0; y < height; y++)
                {
                    token.ThrowIfCancellationRequested();
                    for (var x = 0; x < width; x++)
                    {
                        var pixel = GetPixel(image, x, y);
                        row[x * 4] = Channel(pixel, layout.Blue);
                        row[x * 4 + 1] = Channel(pixel, layout.Green);
                        row[x * 4 + 2] = Channel(pixel, layout.Red);
                        row[x * 4 + 3] = 255;
                    }
                    Marshal.Copy(row, 0, target.Address + y * target.RowBytes, row.Length);
                }
            }
            bitmap.Save(destination, new PngBitmapEncoderOptions());
        }
        finally { DestroyImage(image); }
    }, token);
    private static byte Channel(nuint value, nuint mask)
    {
        if (mask == 0) return 0;
        while ((mask & 1) == 0) { mask >>= 1; value >>= 1; }
        return (byte)((value & mask) * 255 / mask);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ImageLayout
    {
        public int Width, Height, Offset, Format;
        public nint Data;
        public int ByteOrder, BitmapUnit, BitmapBitOrder, BitmapPad, Depth, BytesPerLine, BitsPerPixel;
        public nuint Red, Green, Blue;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Monitor
    {
        public nuint Name;
        public int Primary, Automatic, Outputs, X, Y, Width, Height, MillimeterWidth, MillimeterHeight;
        public nint OutputIds;
    }
    [DllImport("libXrandr.so.2", EntryPoint = "XRRGetMonitors")] private static extern nint GetMonitors(nint display, nuint window, int active, out int count);
    [DllImport("libXrandr.so.2", EntryPoint = "XRRFreeMonitors")] private static extern void FreeMonitors(nint monitors);
    [DllImport(X11Connection.Library, EntryPoint = "XGetGeometry")] private static extern int Geometry(nint display, nuint window, out nuint root, out int x, out int y, out uint width, out uint height, out uint border, out uint depth);
    [DllImport(X11Connection.Library, EntryPoint = "XGetImage")] private static extern nint GetImage(nint display, nuint window, int x, int y, uint width, uint height, nuint planes, int format);
    [DllImport(X11Connection.Library, EntryPoint = "XGetPixel")] private static extern nuint GetPixel(nint image, int x, int y);
    [DllImport(X11Connection.Library, EntryPoint = "XDestroyImage")] private static extern int DestroyImage(nint image);
}
