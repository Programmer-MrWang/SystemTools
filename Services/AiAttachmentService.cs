using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using SystemTools.Models;

namespace SystemTools.Services;

public sealed record AiAttachmentLoadResult(
    IReadOnlyList<AiAttachment> Accepted,
    IReadOnlyList<string> Rejected);

public static class AiAttachmentService
{
    public const int MaximumAttachmentCount = 20;
    public const long MaximumAttachmentBytes = 20L * 1024 * 1024;
    public const long MaximumPendingBytes = 50L * 1024 * 1024;
    public const int MaximumTextCharacters = 2_000_000;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly UnicodeEncoding StrictUtf16Le = new(
        bigEndian: false,
        byteOrderMark: true,
        throwOnInvalidBytes: true);
    private static readonly UnicodeEncoding StrictUtf16Be = new(
        bigEndian: true,
        byteOrderMark: true,
        throwOnInvalidBytes: true);

    public static FilePickerOpenOptions CreateFilePickerOptions()
    {
        return new FilePickerOpenOptions
        {
            Title = "选择要发送给 AI 的文件或图片",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("支持的文件")
                {
                    Patterns =
                    [
                        "*.png", "*.jpg", "*.jpeg", "*.webp", "*.gif", "*.pdf",
                        "*.txt", "*.md", "*.json", "*.xml", "*.yaml", "*.yml",
                        "*.csv", "*.log", "*.cs", "*.axaml", "*.xaml", "*.js",
                        "*.ts", "*.tsx", "*.jsx", "*.html", "*.css", "*.py",
                        "*.java", "*.cpp", "*.c", "*.h", "*.hpp", "*.rs", "*.go",
                        "*.sh", "*.ps1", "*.bat", "*.cmd", "*.toml", "*.ini",
                        "*.props", "*.targets", "*.sln", "*.slnx", "*.csproj"
                    ]
                },
                new FilePickerFileType("所有文件") { Patterns = ["*.*"] }
            ]
        };
    }

    public static async Task<AiAttachmentLoadResult> LoadFilesAsync(
        IReadOnlyList<IStorageFile> files,
        int existingCount,
        long existingBytes,
        CancellationToken cancellationToken = default)
    {
        var accepted = new List<AiAttachment>();
        var rejected = new List<string>();
        var totalBytes = existingBytes;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = SanitizeFileName(file.Name);

            if (existingCount + accepted.Count >= MaximumAttachmentCount)
            {
                rejected.Add($"{fileName}：附件数量超过 {MaximumAttachmentCount} 个");
                continue;
            }

            try
            {
                await using var stream = await file.OpenReadAsync();
                var data = await ReadBytesAsync(stream, MaximumAttachmentBytes, cancellationToken);
                if (data.Length == 0)
                {
                    rejected.Add($"{fileName}：文件为空");
                    continue;
                }

                if (totalBytes + data.LongLength > MaximumPendingBytes)
                {
                    rejected.Add($"{fileName}：待发送附件总大小超过 50 MiB");
                    continue;
                }

                if (!TryCreateAttachment(fileName, data, out var attachment, out var error))
                {
                    rejected.Add($"{fileName}：{error}");
                    continue;
                }

                accepted.Add(attachment!);
                totalBytes += attachment!.Size;
            }
            catch (AttachmentTooLargeException)
            {
                rejected.Add($"{fileName}：单个附件超过 20 MiB");
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                rejected.Add($"{fileName}：无法处理（{ex.Message}）");
            }
        }

        return new AiAttachmentLoadResult(accepted, rejected);
    }

    public static bool TryCreatePastedBitmap(
        Bitmap bitmap,
        int existingCount,
        long existingBytes,
        out AiAttachment? attachment,
        out string? error)
    {
        attachment = null;
        error = null;

        if (existingCount >= MaximumAttachmentCount)
        {
            error = $"粘贴的图片：附件数量超过 {MaximumAttachmentCount} 个";
            return false;
        }

        using var encoded = new MemoryStream();
        bitmap.Save(encoded, PngBitmapEncoderOptions.Default);
        var data = encoded.ToArray();
        if (data.LongLength > MaximumAttachmentBytes)
        {
            error = "粘贴的图片：PNG 数据超过 20 MiB";
            return false;
        }

        if (existingBytes + data.LongLength > MaximumPendingBytes)
        {
            error = "粘贴的图片：待发送附件总大小超过 50 MiB";
            return false;
        }

        attachment = new AiAttachment
        {
            FileName = $"粘贴图片-{DateTime.Now:yyyyMMdd-HHmmss}.png",
            Kind = AiAttachmentKind.Image,
            MediaType = "image/png",
            Size = data.LongLength,
            Data = data
        };
        return true;
    }

    public static bool TryCreateAttachment(
        string fileName,
        byte[] data,
        out AiAttachment? attachment,
        out string? error)
    {
        attachment = null;
        error = null;

        if (data.Length == 0)
        {
            error = "文件为空";
            return false;
        }

        if (data.LongLength > MaximumAttachmentBytes)
        {
            error = "单个附件超过 20 MiB";
            return false;
        }

        if (TryDetectImageType(data, out var imageMediaType))
        {
            try
            {
                using var stream = new MemoryStream(data, writable: false);
                using var decoded = Bitmap.DecodeToWidth(stream, 160);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                error = "图片数据已损坏或无法解码";
                return false;
            }

            attachment = new AiAttachment
            {
                FileName = SanitizeFileName(fileName),
                Kind = AiAttachmentKind.Image,
                MediaType = imageMediaType!,
                Size = data.LongLength,
                Data = data
            };
            return true;
        }

        if (IsPdf(data))
        {
            if (!LooksLikeCompletePdf(data))
            {
                error = "PDF 数据不完整或已损坏";
                return false;
            }

            attachment = new AiAttachment
            {
                FileName = SanitizeFileName(fileName),
                Kind = AiAttachmentKind.Pdf,
                MediaType = "application/pdf",
                Size = data.LongLength,
                Data = data
            };
            return true;
        }

        if (!TryDecodeText(data, out var text, out error))
        {
            return false;
        }

        attachment = new AiAttachment
        {
            FileName = SanitizeFileName(fileName),
            Kind = AiAttachmentKind.Text,
            MediaType = "text/plain; charset=utf-8",
            Size = data.LongLength,
            Text = text
        };
        return true;
    }

    private static async Task<byte[]> ReadBytesAsync(
        Stream stream,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (stream.CanSeek && stream.Length > maximumBytes)
        {
            throw new AttachmentTooLargeException();
        }

        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return output.ToArray();
            }

            if (output.Length + read > maximumBytes)
            {
                throw new AttachmentTooLargeException();
            }

            output.Write(buffer, 0, read);
        }
    }

    private static bool TryDetectImageType(byte[] data, out string? mediaType)
    {
        mediaType = null;
        if (data.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            mediaType = "image/png";
        }
        else if (data.AsSpan().StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }))
        {
            mediaType = "image/jpeg";
        }
        else if (data.Length >= 12 &&
                 data.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
                 data.AsSpan(8, 4).SequenceEqual("WEBP"u8))
        {
            mediaType = "image/webp";
        }
        else if (data.AsSpan().StartsWith("GIF87a"u8) || data.AsSpan().StartsWith("GIF89a"u8))
        {
            mediaType = "image/gif";
        }

        return mediaType is not null;
    }

    private static bool IsPdf(byte[] data)
    {
        return data.AsSpan().StartsWith("%PDF-"u8);
    }

    private static bool LooksLikeCompletePdf(byte[] data)
    {
        var tailLength = Math.Min(data.Length, 4096);
        return data.AsSpan(data.Length - tailLength, tailLength).IndexOf("%%EOF"u8) >= 0;
    }

    private static bool TryDecodeText(byte[] data, out string? text, out string? error)
    {
        text = null;
        error = null;

        try
        {
            if (data.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }))
            {
                if ((data.Length - 2) % 2 != 0)
                {
                    error = "UTF-16 LE 字节数不完整";
                    return false;
                }

                text = StrictUtf16Le.GetString(data, 2, data.Length - 2);
            }
            else if (data.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }))
            {
                if ((data.Length - 2) % 2 != 0)
                {
                    error = "UTF-16 BE 字节数不完整";
                    return false;
                }

                text = StrictUtf16Be.GetString(data, 2, data.Length - 2);
            }
            else
            {
                var offset = data.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? 3 : 0;
                text = StrictUtf8.GetString(data, offset, data.Length - offset);
            }
        }
        catch (DecoderFallbackException)
        {
            error = "不是可严格解码的 UTF-8 或带 BOM 的 UTF-16 文本";
            return false;
        }

        if (text.Length > MaximumTextCharacters)
        {
            error = $"解码后超过 {MaximumTextCharacters:N0} 个字符";
            text = null;
            return false;
        }

        if (text.Any(IsDisallowedControlCharacter))
        {
            error = "文本包含异常二进制控制字符";
            text = null;
            return false;
        }

        return true;
    }

    private static bool IsDisallowedControlCharacter(char value)
    {
        if (!char.IsControl(value))
        {
            return false;
        }

        return value is not '\t' and not '\n' and not '\r';
    }

    private static string SanitizeFileName(string? fileName)
    {
        var name = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(name))
        {
            return "附件";
        }

        var sanitized = new string(name.Where(value => !char.IsControl(value)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "附件" : sanitized;
    }

    private sealed class AttachmentTooLargeException : Exception;
}
