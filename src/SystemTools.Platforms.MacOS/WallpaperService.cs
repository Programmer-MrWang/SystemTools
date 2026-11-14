using SystemTools.Contracts;

namespace SystemTools.Platforms.MacOS;

public sealed class WallpaperService : IDesktopWallpaper
{
    public Capability Probe() => NativeBridge.Probe("wallpaper.probe");
    public async Task<WallpaperState> ReadAsync(CancellationToken token)
    {
        var response = await Task.Run(() => NativeBridge.Require(new { operation = "wallpaper.read" }), token);
        return new("macos:screens", response.GetProperty("snapshot").GetString()!,
            response.TryGetProperty("path", out var path) ? path.GetString() : null);
    }
    public async Task<WallpaperState> ApplyAsync(string imagePath, int fitStyle, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await Task.Run(() => NativeBridge.Require(new { operation = "wallpaper.apply", path = imagePath, fitStyle }), token);
        return await ReadAsync(CancellationToken.None);
    }
    public async Task RestoreAsync(WallpaperState expected, WallpaperState original, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await Task.Run(() => NativeBridge.Require(new { operation = "wallpaper.restore", expected = expected.Snapshot, original = original.Snapshot }), token);
    }
}
