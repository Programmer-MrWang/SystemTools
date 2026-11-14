using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SystemTools.ConfigHandlers;
using SystemTools.Contracts;
using SystemTools.Services;
using Color = Avalonia.Media.Color;

namespace SystemTools;

public sealed class NativeDesktopAppearance(NativeDesktopServices native, MainConfigHandler config,
    ClassIslandSettingsService settings, ILogger<NativeDesktopAppearance> logger) : IHostedService, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _stop;
    private Task? _poll;
    private CancellationTokenRegistration _stopping;
    private Color? _appliedAccent;
    private bool _disposed;
    public Color? CurrentColor { get; private set; }
    public string LastError { get; private set; } = "";
    public bool IsDark => CurrentColor is { } color && (.2126 * color.R + .7152 * color.G + .0722 * color.B) < 128;

    public Task StartAsync(CancellationToken token)
    {
        settings.SubscribeSettingsChanges();
        settings.SettingsPropertyChanged += SettingsChanged;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(native.Stopping);
        _stopping = native.Stopping.Register(Dispose);
        _poll = Poll(_stop.Token);
        return Task.CompletedTask;
    }
    private void SettingsChanged(object? sender, string property)
    {
        if (_appliedAccent is null || property is not ("PrimaryColor" or "ColorSource")) return;
        _appliedAccent = null;
        config.Data.WallpaperAsAccentColorSource = false;
        config.Data.WallpaperAccentOriginalPrimaryColor = null;
        config.Data.WallpaperAccentOriginalColorSource = null;
        config.Save();
    }
    private async Task Poll(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            do
            {
                try
                {
                    if (config.Data.WallpaperAsAccentColorSource || config.Data.FloatingWindowTheme == 3) await RefreshAsync(token);
                    else await Dispatcher.UIThread.InvokeAsync(RestoreAccent);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception error)
                {
                    if (LastError != error.Message) logger.LogWarning(error, "壁纸取色暂不可用，保留现有主题。");
                    LastError = error.Message;
                }
            } while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    public async Task RefreshAsync(CancellationToken token)
    {
        if (native.Wallpaper is null) return;
        await _gate.WaitAsync(token);
        try
        {
            var snapshot = await native.Wallpaper.ReadAsync(token);
            if (string.IsNullOrWhiteSpace(snapshot.ImagePath) || !File.Exists(snapshot.ImagePath))
                throw new IOException("当前壁纸不是可读取的本地图片，已保留现有主题。");
            var color = await Task.Run(() => ReadColor(snapshot.ImagePath), token);
            token.ThrowIfCancellationRequested();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed || native.Stopping.IsCancellationRequested) return;
                CurrentColor = color;
                if (config.Data.WallpaperAsAccentColorSource)
                {
                    if (_appliedAccent is null && config.Data.WallpaperAccentOriginalColorSource is null)
                    {
                        config.Data.WallpaperAccentOriginalPrimaryColor = settings.GetPrimaryColor()?.ToString();
                        config.Data.WallpaperAccentOriginalColorSource = settings.GetColorSource();
                        config.Save();
                    }
                    if (!settings.SetPrimaryColor(color) || !settings.SetColorSource(ClassIslandSettingsService.CustomColorSource))
                        throw new IOException("壁纸已读取，但宿主未提供主题色设置，原设置已保留。");
                    _appliedAccent = color;
                }
                else RestoreAccent();
            });
            LastError = "";
        }
        finally { _gate.Release(); }
    }
    public static Color ReadColor(string path)
    {
        var info = Image.Identify(path) ?? throw new IOException("无法识别壁纸图片。");
        if ((long)info.Width * info.Height > 100_000_000) throw new IOException("壁纸图片过大，请缩小后重试。");
        using var image = Image.Load<Rgba32>(path);
        long red = 0, green = 0, blue = 0, samples = 0;
        for (var y = 0; y < image.Height; y += Math.Max(1, image.Height / 64))
            for (var x = 0; x < image.Width; x += Math.Max(1, image.Width / 64))
            {
                var pixel = image[x, y];
                if (pixel.A < 16) continue;
                red += pixel.R; green += pixel.G; blue += pixel.B; samples++;
            }
        if (samples == 0) throw new IOException("壁纸图片没有可见颜色。");
        return Color.FromRgb((byte)(red / samples), (byte)(green / samples), (byte)(blue / samples));
    }
    private void RestoreAccent()
    {
        if (_appliedAccent is not { } applied) return;
        if (settings.GetPrimaryColor() == applied && settings.GetColorSource() == ClassIslandSettingsService.CustomColorSource)
        {
            if (Color.TryParse(config.Data.WallpaperAccentOriginalPrimaryColor, out var original)) settings.SetPrimaryColor(original);
            if (config.Data.WallpaperAccentOriginalColorSource is { } source) settings.SetColorSource(source);
        }
        _appliedAccent = null;
        config.Data.WallpaperAccentOriginalPrimaryColor = null;
        config.Data.WallpaperAccentOriginalColorSource = null;
        config.Save();
    }
    public async Task StopAsync(CancellationToken token)
    {
        Dispose();
        if (_poll is not null) await _poll.WaitAsync(token);
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _stop?.Cancel(); _stopping.Unregister();
        settings.SettingsPropertyChanged -= SettingsChanged;
        settings.UnsubscribeSettingsChanges();
        if (Dispatcher.UIThread.CheckAccess()) RestoreAccent(); else Dispatcher.UIThread.Post(RestoreAccent);
    }
}
