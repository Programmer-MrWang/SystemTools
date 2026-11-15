using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClassIsland.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SystemTools.Common.Layout;
using SystemTools.ConfigHandlers;
using SystemTools.Contracts;
using SystemTools.Services;

namespace SystemTools;

public sealed partial class NativeBackgroundAppearance(NativeDesktopServices native, MainConfigHandler config,
    ILogger<NativeBackgroundAppearance> logger) : IHostedService
{
    private readonly ClassIslandSettingsService _settings = new();
    private readonly BackgroundThemePolicy _theme = new();
    private CancellationTokenSource? _stop;
    private Task? _poll;
    private (int Theme, bool Override)? _originalTheme;
    private int? _appliedTheme;
    private ScreenRegion? _localRegion;
    private bool _takingOver;
    public string LastError { get; private set; } = "";

    public Task StartAsync(CancellationToken token)
    {
        _stop = CancellationTokenSource.CreateLinkedTokenSource(native.Stopping);
        _settings.SubscribeSettingsChanges();
        _settings.SettingsPropertyChanged += UserChanged;
        _poll = Poll(_stop.Token);
        return Task.CompletedTask;
    }
    private void UserChanged(object? sender, string property)
    {
        if (_takingOver) return;
        if (_appliedTheme is not null && property is "MainWindowTheme" or "IsMainWindowThemeOverrideEnabled" or "Theme")
        {
            _appliedTheme = null; _originalTheme = null;
            config.Data.AutoSwitchClassIslandTheme = false; config.Save();
        }
        VisibilityChanged(property);
    }
    partial void VisibilityChanged(string property);
    partial void RestoreVisibility();
    private async Task Poll(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            do
            {
                try
                {
                    if (config.Data.AutoSwitchClassIslandTheme || config.Data.AutoHideMainWindowWhenOccluded)
                        await RefreshAsync(token);
                    else await Dispatcher.UIThread.InvokeAsync(Restore);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception error)
                {
                    if (LastError != error.Message) logger.LogWarning(error, "背景外观暂不可用，恢复插件应用的状态。");
                    LastError = error.Message;
                    await Dispatcher.UIThread.InvokeAsync(Restore);
                }
            } while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    public async Task RefreshAsync(CancellationToken token)
    {
        var capture = native.Advanced.Background ?? throw new InvalidOperationException("当前平台没有背景捕获后端。");
        var state = capture.Probe(); if (!state.CanExecute) throw new NativeFeatureException(state.State, state.Reason);
        var region = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var window = AppBase.Current.MainWindow;
            var grids = window?.GetVisualDescendants().OfType<Grid>().Where(x => x.Name == "PART_GridWrapper" && x.Bounds.Width > 0 && x.Bounds.Height > 0).ToArray() ?? [];
            if (window is null) return (Handle: (nint)0, Local: (ScreenRegion?)null);
            if (grids.Length != 0)
            {
                var areas = grids.Select(x => new Rect(x.TranslatePoint(default, window) ?? default, x.Bounds.Size)).ToArray();
                var area = areas.Aggregate((a, b) => a.Union(b));
                _localRegion = new(area.X, area.Y, area.Width, area.Height);
            }
            return (Handle: window.TryGetPlatformHandle()?.Handle ?? 0, Local: _localRegion);
        });
        if (region.Handle == 0 || region.Local is null) return;
        if (capture is not IWindowRegionProvider coordinates) throw new InvalidOperationException("缺少原生窗口坐标转换，已停止背景外观。");
        var global = await coordinates.WindowRegionAsync(region.Handle, region.Local, token);
        var frame = await capture.CaptureAsync(global, token);
        if (!frame.ExcludesOwnProcess) throw new IOException("背景包含本进程窗口，已停止自适应外观。");
        var luminance = await Task.Run(() => ReadLuminance(frame), token);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (token.IsCancellationRequested) return;
            if (config.Data.AutoSwitchClassIslandTheme)
            {
                var target = _theme.Observe(luminance, _settings.GetMainWindowTheme());
                if (target is { } theme)
                {
                    _originalTheme ??= _settings.ReadMainWindowTheme();
                    if (_settings.SetMainWindowTheme(theme)) _appliedTheme = theme;
                }
            }
            else RestoreTheme();
        });
        await RecognizeAsync(frame, token);
        LastError = "";
    }
    private Task RecognizeAsync(BackgroundFrame frame, CancellationToken token) => ProcessTextAsync(frame, token);
    private partial Task ProcessTextAsync(BackgroundFrame frame, CancellationToken token);
    public static double ReadLuminance(BackgroundFrame frame)
    {
        using var stream = new MemoryStream(frame.Png, writable: false);
        using var image = NativeRasterImage.Read(stream, 16_000_000);
        double total = 0; var count = 0;
        for (var y = 0; y < image.Height; y += Math.Max(1, image.Height / 64))
            for (var x = 0; x < image.Width; x += Math.Max(1, image.Width / 64))
            { var pixel = image.GetPixel(x, y); if (pixel.Alpha < 16) continue; total += .2126 * pixel.Red + .7152 * pixel.Green + .0722 * pixel.Blue; count++; }
        if (count == 0) throw new IOException("背景没有可见像素。");
        return total / count;
    }
    private void RestoreTheme()
    {
        if (_appliedTheme is { } applied && _settings.GetMainWindowTheme() == applied && _originalTheme is { } original)
            _settings.RestoreMainWindowTheme(original);
        _appliedTheme = null; _originalTheme = null; _theme.Reset();
    }
    private void Restore() { _takingOver = true; try { RestoreTheme(); RestoreVisibility(); } finally { _takingOver = false; } }
    public async Task StopAsync(CancellationToken token)
    {
        _stop?.Cancel();
        if (_poll is not null) await _poll.WaitAsync(token);
        _settings.SettingsPropertyChanged -= UserChanged; _settings.UnsubscribeSettingsChanges();
        await Dispatcher.UIThread.InvokeAsync(Restore);
        _stop?.Dispose();
    }
}
