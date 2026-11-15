using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace SystemTools.Common.Media;

public static class FfmpegMicrophone
{
    public static async IAsyncEnumerable<byte[]> CaptureAsync(string ffmpeg, string device,
        [EnumeratorCancellation] CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var argument in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-f", "pulse", "-i",
                     string.IsNullOrWhiteSpace(device) ? "default" : device, "-ac", "1", "-ar", "16000", "-f", "s16le", "pipe:1" })
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException("无法启动麦克风采集。");
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            while (true)
            {
                var buffer = new byte[3200];
                var count = await process.StandardOutput.BaseStream.ReadAtLeastAsync(buffer, 2, throwOnEndOfStream: false, token).ConfigureAwait(false);
                if (count == 0) break;
                if (count % 2 != 0)
                {
                    var extra = await process.StandardOutput.BaseStream.ReadAsync(buffer.AsMemory(count, 1), token).ConfigureAwait(false);
                    if (extra != 1) throw new IOException("麦克风采样不完整。");
                    count++;
                }
                yield return buffer[..count];
            }
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new IOException("麦克风已断开、被占用或无访问权限。");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await errors.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
    }
}
