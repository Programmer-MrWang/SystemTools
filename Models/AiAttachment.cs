using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Avalonia.Media.Imaging;

namespace SystemTools.Models;

public enum AiAttachmentKind
{
    Image,
    Pdf,
    Text
}

public sealed class AiAttachment : INotifyPropertyChanged, IDisposable
{
    private Bitmap? _thumbnail;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string FileName { get; set; } = "附件";

    public AiAttachmentKind Kind { get; set; }

    public string MediaType { get; set; } = "application/octet-stream";

    public long Size { get; set; }

    public byte[]? Data { get; set; }

    public string? Text { get; set; }

    [JsonIgnore]
    public bool IsImage => Kind == AiAttachmentKind.Image;

    [JsonIgnore]
    public bool IsFile => !IsImage;

    [JsonIgnore]
    public string KindLabel => Kind switch
    {
        AiAttachmentKind.Pdf => "PDF",
        AiAttachmentKind.Text => "文本",
        _ => "图片"
    };

    [JsonIgnore]
    public string SizeText => FormatSize(Size);

    [JsonIgnore]
    public string DetailText => $"{KindLabel} · {SizeText}";

    [JsonIgnore]
    public Bitmap? Thumbnail
    {
        get
        {
            if (!IsImage || Data is not { Length: > 0 })
            {
                return null;
            }

            if (_thumbnail is not null)
            {
                return _thumbnail;
            }

            try
            {
                using var stream = new MemoryStream(Data, writable: false);
                _thumbnail = Bitmap.DecodeToWidth(stream, 160);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return null;
            }

            return _thumbnail;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Dispose()
    {
        _thumbnail?.Dispose();
        _thumbnail = null;
    }

    public void NotifyRuntimePropertiesChanged()
    {
        OnPropertyChanged(nameof(IsImage));
        OnPropertyChanged(nameof(IsFile));
        OnPropertyChanged(nameof(KindLabel));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(DetailText));
        OnPropertyChanged(nameof(Thumbnail));
    }

    private static string FormatSize(long size)
    {
        if (size < 1024)
        {
            return $"{size} B";
        }

        if (size < 1024 * 1024)
        {
            return $"{size / 1024d:0.#} KiB";
        }

        return $"{size / (1024d * 1024d):0.#} MiB";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
