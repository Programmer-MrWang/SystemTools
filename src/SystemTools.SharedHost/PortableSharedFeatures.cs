using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.Core.Models.Automation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SystemTools.Actions;
using SystemTools.Contracts;
using SystemTools.Controls;
using SystemTools.Settings;
using SystemTools.Shared;
using SystemTools.Triggers;
using ClassIsland.Shared;

namespace SystemTools;

public static class PortableSharedFeatures
{
    public static void Register(IServiceCollection services, FeatureCatalog catalog)
    {
        foreach (var suffix in new[] { "ActionFlowExecutionConfirmation", "BackgroundPlayAudio", "BlackScreenHtml" })
            catalog.BindForRegistration("SystemTools." + suffix, () => GlobalConstants.MainConfig!.Data.IsActionEnabled("SystemTools." + suffix)
                ? new(CapabilityState.Available) : new(CapabilityState.Disabled, "用户已关闭。"));
        if (catalog.CanCreate("SystemTools.ActionFlowExecutionConfirmation")) services.AddAction<ActionFlowExecutionConfirmationAction, ActionFlowExecutionConfirmationSettingsControl>();
        if (catalog.CanCreate("SystemTools.BackgroundPlayAudio")) services.AddAction<PortableAudioAction, PortableAudioSettingsControl>();
        if (catalog.CanCreate("SystemTools.BlackScreenHtml")) services.AddAction<PortableBlackScreenAction, ShortcutKeyNotificationSettingsControl>();
        foreach (var suffix in new[] { "ActionInProgressTrigger", "MainWindowClickTrigger" })
            catalog.BindForRegistration("SystemTools." + suffix, () => GlobalConstants.MainConfig!.Data.IsTriggerEnabled("SystemTools." + suffix)
                ? new(CapabilityState.Available) : new(CapabilityState.Disabled, "用户已关闭。"));
        if (catalog.CanCreate("SystemTools.ActionInProgressTrigger")) services.AddTrigger<PortableActionSignalTrigger, ActionInProgressTriggerSettings>();
        if (catalog.CanCreate("SystemTools.MainWindowClickTrigger")) services.AddTrigger<PortableMainClickTrigger, PortableMainClickSettings>();
        services.AddSingleton<PortableSurfaceLifetime>();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<PortableSurfaceLifetime>());

    }
}

public sealed class PortableSurfaceLifetime : IHostedService
{
    private readonly HashSet<Window> _windows = [];
    private readonly Dictionary<object,Window> _owned = new(ReferenceEqualityComparer.Instance);
    private readonly CancellationTokenSource _stop = new();
    public CancellationToken Stopping => _stop.Token;
    public void Track(Window window) { _windows.Add(window); window.Closed += (_, _) => _windows.Remove(window); }
    public void Track(Window window,object owner)
    {
        Close(owner);_owned[owner]=window;Track(window);
        window.Closed+=(_,_)=>{if(_owned.TryGetValue(owner,out var current)&&ReferenceEquals(current,window))_owned.Remove(owner);};
    }
    public void Close(object owner){if(_owned.Remove(owner,out var window))window.Close();}
    public Task StartAsync(CancellationToken token) => Task.CompletedTask;
    public async Task StopAsync(CancellationToken token)
    {
        _stop.Cancel();
        await Dispatcher.UIThread.InvokeAsync(() => { foreach (var window in _windows.ToArray()) window.Close(); _windows.Clear(); });
    }
}

[ActionInfo("SystemTools.BackgroundPlayAudio", "后台播放音频", "\uEBCC", false)]
public sealed class PortableAudioAction(PortableSurfaceLifetime lifetime, ILogger<PortableAudioAction> logger, ClassIsland.Core.Abstractions.Services.IAudioService audio) : ActionBase<BackgroundPlayAudioSettings>
{
    private CancellationTokenSource? _playback;
    private readonly object _gate = new();
    protected override async Task OnInvoke()
    {
        var path = Path.GetFullPath(Settings.AudioFilePath);
        if (!File.Exists(path)) throw new FileNotFoundException("请选择存在的音频文件。", path);
        lock (_gate) _playback?.Cancel();
        // 宿主正常结束行动组也会取消其令牌；后台播放须独立存活至完成或插件停止。
        var stop = Settings.WaitForPlaybackCompleted
            ? CancellationTokenSource.CreateLinkedTokenSource(lifetime.Stopping, InterruptCancellationToken)
            : CancellationTokenSource.CreateLinkedTokenSource(lifetime.Stopping);
        lock (_gate) _playback = stop;
        async Task Play()
        {
            try
            {
                await using var stream = File.OpenRead(path);
                await audio.PlayAudioAsync(stream, 1, stop.Token);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            finally { lock (_gate) { if (ReferenceEquals(_playback, stop)) _playback = null; stop.Dispose(); } }
        }
        var playback = Play();
        if (Settings.WaitForPlaybackCompleted) await playback;
        else _ = playback.ContinueWith(task => { if (task.Exception is { } error) logger.LogWarning(error, "后台音频播放失败。"); }, TaskScheduler.Default);
        NativeActionFeedback.Notify(Settings.NotifyOnExecute, "已播放音频");
    }
    protected override Task OnInterrupted() { lock (_gate) _playback?.Cancel(); return Task.CompletedTask; }
}

public sealed class PortableAudioSettingsControl : ClassIsland.Core.Abstractions.Controls.ActionSettingsControlBase<BackgroundPlayAudioSettings>
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        var path = new TextBox { PlaceholderText = "音频文件完整路径（支持中文）", Text = Settings.AudioFilePath };
        path.TextChanged += (_, _) => Settings.AudioFilePath = path.Text ?? "";
        var wait = new CheckBox { Content = "等待播放完成", IsChecked = Settings.WaitForPlaybackCompleted };
        wait.IsCheckedChanged += (_, _) => Settings.WaitForPlaybackCompleted = wait.IsChecked == true;
        var notify = new CheckBox { Content = "执行时提醒", IsChecked = Settings.NotifyOnExecute };
        notify.IsCheckedChanged += (_, _) => Settings.NotifyOnExecute = notify.IsChecked == true;
        Content = new StackPanel { Margin = new Thickness(10), Spacing = 8, Children = { path, wait, notify } };
    }
}

[ActionInfo("SystemTools.BlackScreenHtml", "黑屏", "\uE643", false)]
public sealed class PortableBlackScreenAction(PortableSurfaceLifetime lifetime) : ActionBase<ShortcutKeyNotificationSettings>
{
    private Window? _window;
    protected override async Task OnInvoke()
    {
        InterruptCancellationToken.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            lifetime.Close(ActionItem);
            var window = new Window { Title = "SystemTools 黑屏 · Esc 或单击退出", Background = Brushes.Black, WindowState = WindowState.FullScreen,
                Topmost = true, ShowInTaskbar = false, WindowDecorations = WindowDecorations.None };
            window.KeyDown += (_, args) => { if (args.Key == Key.Escape) window.Close(); };
            window.PointerPressed += (_, _) => window.Close();
            lifetime.Track(window,ActionItem); _window = window; window.Show();
        });
    }
    protected override async Task OnInterrupted() => await Dispatcher.UIThread.InvokeAsync(() => lifetime.Close(ActionItem));
    protected override Task OnRevert() => OnInterrupted();
}

[TriggerInfo("SystemTools.ActionInProgressTrigger", "行动进行时", "\uEAB7")]
public sealed class PortableActionSignalTrigger(ILogger<PortableActionSignalTrigger> logger) : TriggerBase<ActionInProgressTriggerConfig>
{
    private CancellationTokenSource? _stop;
    private Task? _poll;
    private static readonly object Gate = new();
    public override void Loaded() { UnLoaded(); _stop = new(); _poll = Poll(_stop.Token); }
    public override void UnLoaded()
    {
        var stop = Interlocked.Exchange(ref _stop, null); stop?.Cancel();
        if (stop is not null) _ = (_poll ?? Task.CompletedTask).ContinueWith(_ => stop.Dispose(), TaskScheduler.Default);
    }
    private async Task Poll(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                try
                {
                    var path = Path.Combine(GlobalConstants.Information.PluginFolder!, "auto.json");
                    var matched = false;
                    lock (Gate)
                    {
                        if (!File.Exists(path) || string.IsNullOrWhiteSpace(Settings.TriggerId)) continue;
                        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
                        if (document.RootElement.TryGetProperty("TriggerId", out var id) && id.GetString() == Settings.TriggerId)
                        { File.Delete(path); matched = true; }
                    }
                    if (matched && !token.IsCancellationRequested) await Dispatcher.UIThread.InvokeAsync(Trigger);
                }
                catch (Exception error) when (error is IOException or System.Text.Json.JsonException) { logger.LogDebug("行动信号写入尚未完成，稍后重试。"); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}

[TriggerInfo("SystemTools.MainWindowClickTrigger", "点击主界面时", "\uE5C3")]
public sealed class PortableMainClickTrigger : TriggerBase<MainWindowClickTriggerConfig>
{
    private Window? _window;
    public override void Loaded()
    {
        UnLoaded(); _window = AppBase.Current.MainWindow;
        _window?.AddHandler(InputElement.PointerPressedEvent, Clicked, RoutingStrategies.Tunnel, handledEventsToo: true);
    }
    private void Clicked(object? sender, PointerPressedEventArgs args) { if (args.GetCurrentPoint(_window!).Properties.IsLeftButtonPressed) Trigger(); }
    public override void UnLoaded() { _window?.RemoveHandler(InputElement.PointerPressedEvent, Clicked); _window = null; }
}
public sealed class PortableMainClickSettings : ClassIsland.Core.Abstractions.Controls.TriggerSettingsControlBase<MainWindowClickTriggerConfig>
{
    public PortableMainClickSettings() => Content = new TextBlock { Text = "点击可交互的 ClassIsland 主界面时触发；鼠标穿透模式不会接收点击。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10) };
}
