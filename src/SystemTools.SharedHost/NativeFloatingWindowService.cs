using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Shared;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SystemTools.Common.Layout;
using SystemTools.ConfigHandlers;
using SystemTools.Contracts;
using SystemTools.Services;

namespace SystemTools;

public sealed class NativeFloatingWindowService(MainConfigHandler config, FloatingWindowProfileManager profiles,
    NativeDesktopServices native, ILogger<NativeFloatingWindowService> logger) : IHostedService
{
    private readonly Dictionary<NativeFloatingTrigger, string> _entries = [];
    private Window? _window;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _stopped;
    private bool _refreshQueued;
    private int _layer = -1;
    private string _profile = "";
    private CancellationTokenRegistration _stopping;
    public string LastError { get; private set; } = "";
    public FloatingWindowProfileManager Profiles => profiles;
    public int EntriesCount => _entries.Count;
    public Task StartAsync(CancellationToken token)
    {
        _stopping = native.Stopping.Register(() => StopAsync(CancellationToken.None).GetAwaiter().GetResult());
        config.Data.PropertyChanged += ConfigChanged;
        _timer.Tick += TimerTick;
        Dispatcher.UIThread.Post(() => { if (!_stopped) { profiles.MigrateFromLegacyConfig(config.Data); _timer.Start(); Refresh(); } });
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken token)
    {
        if (_stopped) return Task.CompletedTask;
        _stopped = true;
        _stopping.Unregister();
        config.Data.PropertyChanged -= ConfigChanged;
        void Close()
        {
            _timer.Stop(); _timer.Tick -= TimerTick;
            foreach (var trigger in _entries.Keys.ToArray()) trigger.UnLoaded();
            _entries.Clear(); _window?.Close(); _window = null;
        }
        if (Dispatcher.UIThread.CheckAccess()) Close(); else return Dispatcher.UIThread.InvokeAsync(Close).GetTask();
        return Task.CompletedTask;
    }
    private void TimerTick(object? sender, EventArgs args) => Refresh();
    private void ConfigChanged(object? sender, PropertyChangedEventArgs args) => QueueRefresh();
    public void QueueRefresh()
    {
        if (_refreshQueued || _stopped) return;
        _refreshQueued = true;
        Dispatcher.UIThread.Post(() => { _refreshQueued = false; if (!_stopped) Refresh(); });
    }
    internal void Register(NativeFloatingTrigger trigger)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_stopped || !trigger.IsLoaded) return;
            var id = trigger.Configuration.ButtonId;
            if (string.IsNullOrWhiteSpace(id) || _entries.Any(x => x.Key != trigger && x.Value == id))
                trigger.Configuration.ButtonId = id = Guid.NewGuid().ToString("N");
            _entries[trigger] = id;
            Refresh();
        });
    }
    internal void Unregister(NativeFloatingTrigger trigger) => Dispatcher.UIThread.Post(() => { _entries.Remove(trigger); if (!_stopped) Refresh(); });

    public void ValidateLayer(int layer)
    {
        if (layer is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(layer));
        if (layer == 0)
        {
            var capability = native.Windows?.ProbeBottommost() ?? new(CapabilityState.Unsupported, "当前平台没有置底实现。");
            if (!capability.CanExecute) throw new NativeFeatureException(capability.State, capability.Reason);
        }
    }
    public void SwitchProfile(string name)
    {
        if (!profiles.ProfileFileExists(name)) throw new FileNotFoundException("悬浮窗方案不存在，请重新选择。", name);
        config.Data.CurrentFloatingWindowProfile = name;
        config.Save(); QueueRefresh();
    }
    public void Refresh()
    {
        if (_stopped || native.Stopping.IsCancellationRequested) return;
        try { RefreshCore(); }
        catch (Exception error) { LastError = error.Message; logger.LogWarning(error, "悬浮窗更新失败，原配置已保留。"); }
    }
    private void RefreshCore()
    {
        var data = config.Data;
        if (_profile != data.CurrentFloatingWindowProfile)
        {
            profiles.LoadProfile(data.CurrentFloatingWindowProfile); _profile = data.CurrentFloatingWindowProfile;
        }
        if (!data.EnableFloatingWindowFeature || !data.ShowFloatingWindow || _entries.Count == 0)
        { _window?.Hide(); return; }
        var rules = IAppHost.GetService<IRulesetService>();
        if (data.FloatingWindowRulesetEnabled && rules.IsRulesetSatisfied(data.FloatingWindowRuleset)) { _window?.Hide(); return; }
        if (_window is null)
        {
            _window = new Window { Title = "SystemTools 悬浮窗", ShowInTaskbar = false, CanResize = false,
                WindowDecorations = WindowDecorations.None, SizeToContent = SizeToContent.WidthAndHeight };
            _window.Position = new PixelPoint(data.FloatingWindowPositionX, data.FloatingWindowPositionY);
            _window.PointerPressed += (_, args) => { if (args.GetCurrentPoint(_window).Properties.IsLeftButtonPressed && args.Source == _window) _window.BeginMoveDrag(args); };
            _window.PositionChanged += (_, _) => { if (_stopped) return; data.FloatingWindowPositionX = _window.Position.X; data.FloatingWindowPositionY = _window.Position.Y; };
            _window.Closed += (_, _) => { _window = null; _layer = -1; };
        }
        var profile = profiles.CurrentProfile;
        var entries = _entries.Where(entry => entry.Key.IsLoaded && entry.Key.Configuration.IsVisible).ToDictionary(entry => entry.Value, entry => entry.Key);
        bool Visible(string id) => !profile.FloatingWindowButtonRulesets.TryGetValue(id, out var item) ||
            item.IsVisible && (!item.HideOnRule || !rules.IsRulesetSatisfied(item.HidingRules));
        var visible = entries.Keys.Where(Visible).ToArray();
        var explicitlyConfigured = profile.FloatingWindowButtonRows.SelectMany(row => row).Concat(profile.FloatingWindowButtonOrder).ToHashSet();
        var configuredRows = profile.FloatingWindowButtonRows.Count > 0 ? profile.FloatingWindowButtonRows
            : new List<List<string>> { profile.FloatingWindowButtonOrder };
        var rows = FloatingWindowRows.WithFallback(configuredRows, visible, explicitlyConfigured);
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(8) };
        for (var index = 0; index < rows.Count; index++)
        {
            if (index < profile.FloatingWindowRowRulesets.Count && profile.FloatingWindowRowRulesets[index] is { } rowRules &&
                (!rowRules.IsVisible || rowRules.HideOnRule && rules.IsRulesetSatisfied(rowRules.HidingRules))) continue;
            var row = new StackPanel { Spacing = 6, Orientation = profile.FloatingWindowHorizontal ? Orientation.Horizontal : Orientation.Vertical };
            foreach (var id in rows[index])
            {
                if (!entries.TryGetValue(id, out var trigger)) continue;
                var on = trigger.IsWaitingForRevert;
                var content = new StackPanel { Spacing = 4, Orientation = Orientation.Horizontal };
                content.Children.Add(FloatingWindowIconProvider.CreateIconControl(trigger.Configuration.Icon, data.FloatingWindowIconSize * data.FloatingWindowScale));
                content.Children.Add(new TextBlock { Text = on ? "恢复" : trigger.Configuration.ButtonName, FontSize = data.FloatingWindowTextSize * data.FloatingWindowScale });
                var button = new Button { Content = content };
                button.Click += (_, _) => trigger.InvokeFromWindow();
                row.Children.Add(button);
            }
            if (row.Children.Count != 0) panel.Children.Add(row);
        }
        if (panel.Children.Count == 0) { _window.Hide(); return; }
        _window.Content = panel;
        _window.Opacity = data.FloatingWindowOpacity / 100d;
        _window.RequestedThemeVariant = data.FloatingWindowTheme switch { 1 => ThemeVariant.Light, 2 => ThemeVariant.Dark, _ => ThemeVariant.Default };
        _window.Show();
        if (_layer != data.FloatingWindowLayer)
        {
            _window.Topmost = data.FloatingWindowLayer == 1;
            if (native.Windows?.ProbeBottommost().CanExecute == true)
            {
                native.Windows.SetBottommostAsync(_window.TryGetPlatformHandle()?.Handle ?? 0, data.FloatingWindowLayer == 0, native.Stopping).GetAwaiter().GetResult();
                LastError = "";
            }
            else if (data.FloatingWindowLayer == 0) LastError = "保存的置底设置在当前平台不可用，浮窗暂以普通层级显示。";
            _layer = data.FloatingWindowLayer;
        }
    }
}
