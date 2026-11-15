using System.Buffers.Binary;
using SystemTools.Contracts;

namespace SystemTools.Common.Media;

public static class MediaFiles
{
    public static string? FindExecutable(string name, string configured = "")
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return File.Exists(configured) ? Path.GetFullPath(configured) : null;
        foreach (var root in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            var path = Path.Combine(root.Trim('"'), name + (OperatingSystem.IsWindows() ? ".exe" : ""));
            if (File.Exists(path)) return path;
        }
        return null;
    }

    public static bool IsVoskModel(string path) => Directory.Exists(path) &&
        File.Exists(Path.Combine(path, "am/final.mdl")) && File.Exists(Path.Combine(path, "conf/mfcc.conf"));

    public static void RequirePng(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[24];
        stream.ReadExactly(header);
        if (!header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            !header.Slice(12, 4).SequenceEqual("IHDR"u8) ||
            BinaryPrimitives.ReadUInt32BigEndian(header[16..]) == 0 ||
            BinaryPrimitives.ReadUInt32BigEndian(header[20..]) == 0 || stream.Length < 57)
            throw new IOException("摄像头没有返回完整图片，请检查设备并重试。");
    }

    public static async Task<string> CaptureAsync(ICameraCapture camera, string device, string folder, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var output = Path.GetFullPath(folder);
        Directory.CreateDirectory(output);
        var name = "摄像头抓拍" + DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss") + "-" + Guid.NewGuid().ToString("N");
        var temporary = Path.Combine(output, "." + name + ".png");
        var destination = Path.Combine(output, name + ".png");
        try
        {
            await camera.CapturePngAsync(device, temporary, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            RequirePng(temporary);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: false);
            return destination;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
