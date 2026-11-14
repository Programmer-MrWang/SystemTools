using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Data;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using SystemTools.Common.Automation;
using SystemTools.Contracts;
using SystemTools.Controls;
using SystemTools.Triggers;

namespace SystemTools;

[TriggerInfo("SystemTools.HotkeyTrigger", "按下自定义热键时", "\uEA0B")]
public sealed class NativeHotkeyTrigger(IGlobalHotkeys hotkeys, NativeDesktopServices native, ILogger<NativeHotkeyTrigger> logger) : TriggerBase<HotkeyTriggerConfig>
{
    private IDisposable? _registration;
    private CancellationTokenRegistration _stopping;
    private readonly object _gate = new();
    private bool _loaded;
    private long _last;
    public override void Loaded()
    {
        UnLoaded(); _loaded = true;
        Settings.PropertyChanged += Changed;
        _stopping = native.Stopping.Register(UnLoaded);
        Register();
    }
    private void Changed(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(Settings.LogicalKey) or nameof(Settings.VirtualKey) or nameof(Settings.ModifierKeys)) Register();
    }
    private void Register()
    {
        lock (_gate)
        {
            _registration?.Dispose(); _registration = null;
            if (!_loaded || native.Stopping.IsCancellationRequested) return;
            try
            {
                var key = string.IsNullOrWhiteSpace(Settings.LogicalKey) ? PortableKeyboard.FromWindowsKey(Settings.VirtualKey == 0 ? 0x78 : Settings.VirtualKey) : PortableKeyboard.Normalize(Settings.LogicalKey);
                _registration = hotkeys.Register(key, Settings.ModifierKeys, () =>
                {
                    var now = Environment.TickCount64;
                    if (!_loaded || native.Stopping.IsCancellationRequested || now - Interlocked.Read(ref _last) < 500) return;
                    Interlocked.Exchange(ref _last, now); Trigger();
                });
                Settings.RegistrationStatus = "热键已注册";
            }
            catch (Exception error) { Settings.RegistrationStatus = "无法注册热键：" + error.Message; logger.LogWarning(error, "热键注册失败，请更换组合键。"); }
        }
    }
    public override void UnLoaded()
    {
        lock (_gate)
        {
            _loaded = false;
            Settings.PropertyChanged -= Changed;
            _registration?.Dispose(); _registration = null;
            _stopping.Unregister();
        }
    }
}

public sealed class NativeHotkeySettingsControl : TriggerSettingsControlBase<HotkeyTriggerConfig>
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        var recorder = new HotkeyRecorderControl();
        recorder.SetHotkey(Settings.ModifierKeys, Settings.VirtualKey, Settings.HotkeyDisplay);
        recorder.HotkeyRecorded += (_, args) =>
        {
            Settings.LogicalKey = PortableKeyboard.FromWindowsKey(args.VirtualKey);
            Settings.ModifierKeys = args.ModifierKeys;
            Settings.VirtualKey = args.VirtualKey;
            Settings.HotkeyDisplay = Settings.LogicalKey;
            recorder.HotkeyDisplay = Settings.LogicalKey;
        };
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(Settings.RegistrationStatus)) { Source = Settings });
        Content = new StackPanel { Spacing = 8, Children = { recorder, status } };
    }
}
