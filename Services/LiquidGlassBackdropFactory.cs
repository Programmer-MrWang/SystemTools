using System;
using System.Buffers;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AvaloniaPixelFormat = Avalonia.Platform.PixelFormat;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using DrawingRectangle = System.Drawing.Rectangle;

namespace SystemTools.Services;

internal static class LiquidGlassBackdropFactory
{
    public static WriteableBitmap? Update(
        MainWindowBackgroundFrame? frame,
        WriteableBitmap? reusableBitmap)
    {
        if (frame is null || frame.Regions.Count == 0)
        {
            return null;
        }

        var union = frame.Regions
            .Skip(1)
            .Aggregate(
                frame.Regions[0].Area,
                (current, region) => DrawingRectangle.Union(current, region.Area));
        if (union.Width <= 0 || union.Height <= 0)
        {
            return null;
        }

        var pixelSize = new PixelSize(union.Width, union.Height);
        if (reusableBitmap is null || reusableBitmap.PixelSize != pixelSize)
        {
            reusableBitmap?.Dispose();
            reusableBitmap = new WriteableBitmap(
                pixelSize,
                new Vector(96, 96),
                AvaloniaPixelFormat.Bgra8888,
                AlphaFormat.Premul);
        }

        using var target = reusableBitmap.Lock();
        var maximumRowBytes = Math.Max(
            union.Width * 4,
            frame.Regions.Max(region => region.Bitmap.Width * 4));
        var rowBuffer = ArrayPool<byte>.Shared.Rent(maximumRowBytes);
        try
        {
            Array.Clear(rowBuffer, 0, union.Width * 4);
            for (var y = 0; y < union.Height; y++)
            {
                Marshal.Copy(
                    rowBuffer,
                    0,
                    IntPtr.Add(target.Address, y * target.RowBytes),
                    union.Width * 4);
            }

            foreach (var region in frame.Regions)
            {
                CopyRegion(region, union, target.Address, target.RowBytes, rowBuffer);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rowBuffer, clearArray: true);
        }

        return reusableBitmap;
    }

    private static void CopyRegion(
        MainWindowBackgroundRegion region,
        DrawingRectangle union,
        IntPtr destinationAddress,
        int destinationStride,
        byte[] rowBuffer)
    {
        var source = region.Bitmap;
        var bounds = new DrawingRectangle(0, 0, source.Width, source.Height);
        var data = source.LockBits(
            bounds,
            ImageLockMode.ReadOnly,
            DrawingPixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = source.Width * 4;
            var destinationX = region.Area.Left - union.Left;
            var destinationY = region.Area.Top - union.Top;
            for (var y = 0; y < source.Height; y++)
            {
                var sourceRow = IntPtr.Add(data.Scan0, y * data.Stride);
                var destinationRow = IntPtr.Add(
                    destinationAddress,
                    (destinationY + y) * destinationStride + destinationX * 4);
                Marshal.Copy(sourceRow, rowBuffer, 0, rowBytes);
                Marshal.Copy(rowBuffer, 0, destinationRow, rowBytes);
            }
        }
        finally
        {
            source.UnlockBits(data);
        }
    }
}
