using System.Collections.Concurrent;
using Avalonia.Media;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SystemTools.Common.Automation;
using SystemTools.Contracts;
using SystemTools.Settings;
using SystemTools.Shared;

namespace SystemTools;

[ActionInfo("SystemTools.ChangeWallpaper", "切换壁纸", "\uE9BC", false)]
public sealed class NativeWallpaperAction(IDesktopWallpaper wallpaper, NativeDesktopServices native, FeatureCatalog catalog,
    NativeDesktopAppearance appearance) : ActionBase<ChangeWallpaperSettings>
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly ActionRestoreStore<WallpaperState> Snapshots = new();
    private static readonly ConcurrentDictionary<object, WallpaperState> Applied = new(ReferenceEqualityComparer.Instance);
    protected override async Task OnInvoke()
    {
        catalog.EnsureExecutable(ActionItem.Id);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(InterruptCancellationToken, native.Stopping);
        await Gate.WaitAsync(stop.Token);
        try
        {
            var path = await PrepareImageAsync(stop.Token);
            var original = await wallpaper.ReadAsync(stop.Token);
            var result = await wallpaper.ApplyAsync(path, Settings.FitStyle, stop.Token);
            if (stop.IsCancellationRequested)
            {
                await wallpaper.RestoreAsync(result, original, CancellationToken.None);
                stop.Token.ThrowIfCancellationRequested();
            }
            if (IsRevertable)
            {
                Snapshots.Capture(ActionSet.Guid, ActionItem, original.Identity, original);
                Applied[ActionItem] = result;
            }
            try { await appearance.RefreshAsync(stop.Token); }
            catch (Exception error) when (error is not OperationCanceledException)
            { throw new IOException("壁纸已更新，但取色或主题联动未完成，请检查图片与主题设置。", error); }
            NativeActionFeedback.Notify(Settings.NotifyOnExecute, "桌面壁纸已更新");
        }
        finally { Gate.Release(); }
    }
    protected override async Task OnRevert()
    {
        await Gate.WaitAsync();
        try
        {
            if (!Applied.TryRemove(ActionItem, out var expected)) return;
            if (!Snapshots.TryRestore(ActionSet.Guid, ActionItem, out var original)) return;
            await wallpaper.RestoreAsync(expected, original, CancellationToken.None);
            await appearance.RefreshAsync(CancellationToken.None);
        }
        finally { Gate.Release(); }
    }
    private async Task<string> PrepareImageAsync(CancellationToken token)
    {
        if (Settings.Mode == ChangeWallpaperMode.Image)
        {
            var path = Path.GetFullPath(Settings.ImagePath);
            await Task.Run(() => NativeDesktopAppearance.ReadColor(path), token);
            return path;
        }
        if (Settings.Mode != ChangeWallpaperMode.SolidColor) throw new ArgumentException("壁纸模式无效。");
        var text = Settings.SolidColor;
        if (!Avalonia.Media.Color.TryParse(text, out var color))
        {
            var parts = text.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 3 || !byte.TryParse(parts[0], out var r) || !byte.TryParse(parts[1], out var g) || !byte.TryParse(parts[2], out var b)) throw new FormatException("颜色请使用 #RRGGBB 或 R,G,B。");
            color = Avalonia.Media.Color.FromRgb(r, g, b);
        }
        var directory = Path.Combine(GlobalConstants.PluginConfigFolder!, "WallpaperImages"); Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, $"solid-{color.R:x2}{color.G:x2}{color.B:x2}.png");
        if (!File.Exists(target))
        {
            var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using var image = new Image<Rgba32>(16, 16, new Rgba32(color.R, color.G, color.B));
                await image.SaveAsPngAsync(temporary, token);
                File.Move(temporary, target, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return target;
    }
}
