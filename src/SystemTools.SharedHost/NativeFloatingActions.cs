using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using SystemTools.Common.Automation;
using SystemTools.Contracts;
using SystemTools.Settings;
using SystemTools.Shared;

namespace SystemTools;

public abstract class NativeFloatingAction<TSettings, TValue>(NativeFloatingWindowService service, FeatureCatalog catalog) : ActionBase<TSettings> where TSettings : class, new()
{
    private static readonly ActionRestoreStore<TValue> Snapshots = new();
    protected NativeFloatingWindowService Service => service;
    protected abstract TValue Read();
    protected abstract TValue Target();
    protected abstract void Write(TValue value);
    protected abstract bool NotifyEnabled { get; }
    protected override async Task OnInvoke()
    {
        catalog.EnsureExecutable(ActionItem.Id);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var target = Target();
            if (IsRevertable) Snapshots.Capture(ActionSet.Guid, ActionItem, ActionItem.Id, Read());
            Write(target); GlobalConstants.MainConfig!.Save(); service.Refresh();
            NativeActionFeedback.Notify(NotifyEnabled, "悬浮窗设置已更新");
        });
    }
    protected override async Task OnRevert()
    {
        if (Snapshots.TryRestore(ActionSet.Guid, ActionItem, out var original))
            await Dispatcher.UIThread.InvokeAsync(() => { Write(original); GlobalConstants.MainConfig!.Save(); service.Refresh(); });
    }
}
[ActionInfo("SystemTools.ShowFloatingWindow", "显示悬浮窗", "\uEA37", false)]
public sealed class NativeShowFloatingAction(NativeFloatingWindowService service, FeatureCatalog catalog) : NativeFloatingAction<ShowFloatingWindowSettings, bool>(service, catalog)
{
    protected override bool NotifyEnabled => Settings.NotifyOnExecute;
    protected override bool Read() => GlobalConstants.MainConfig!.Data.ShowFloatingWindow;
    protected override bool Target() => Settings.ShowFloatingWindow && Service.EntriesCount > 0;
    protected override void Write(bool value) => GlobalConstants.MainConfig!.Data.ShowFloatingWindow = value;
}
[ActionInfo("SystemTools.ToggleFloatingWindowLayer", "切换悬浮窗层级", "\uE9A8", false)]
public sealed class NativeFloatingLayerAction(NativeFloatingWindowService service, FeatureCatalog catalog) : NativeFloatingAction<ToggleFloatingWindowLayerSettings, int>(service, catalog)
{
    protected override bool NotifyEnabled => Settings.NotifyOnExecute;
    protected override int Read() => GlobalConstants.MainConfig!.Data.FloatingWindowLayer;
    protected override int Target() { var layer = Settings.TargetLayer < 0 ? 1 - Read() : Settings.TargetLayer; Service.ValidateLayer(layer); return layer; }
    protected override void Write(int value) => GlobalConstants.MainConfig!.Data.FloatingWindowLayer = value;
}
[ActionInfo("SystemTools.SwitchFloatingWindowTheme", "切换悬浮窗主题", "\uE790", false)]
public sealed class NativeFloatingThemeAction(NativeFloatingWindowService service, FeatureCatalog catalog) : NativeFloatingAction<SwitchFloatingWindowThemeSettings, int>(service, catalog)
{
    protected override bool NotifyEnabled => Settings.NotifyOnExecute;
    protected override int Read() => GlobalConstants.MainConfig!.Data.FloatingWindowTheme;
    protected override int Target()
    {
        var value = Settings.TargetTheme < 0 ? (Read() + 1) % 3 : Settings.TargetTheme;
        if (value is < 0 or > 2) throw new NativeFeatureException(CapabilityState.Unsupported, "自适应背景主题需接入壁纸取色，请先选择跟随、浅色或深色。");
        return value;
    }
    protected override void Write(int value) => GlobalConstants.MainConfig!.Data.FloatingWindowTheme = value;
}
[ActionInfo("SystemTools.ToggleFloatingWindowProfile", "切换悬浮窗配置方案", "\uE9A8", false)]
public sealed class NativeFloatingProfileAction(NativeFloatingWindowService service, FeatureCatalog catalog) : NativeFloatingAction<ToggleFloatingWindowProfileSettings, string>(service, catalog)
{
    protected override bool NotifyEnabled => Settings.NotifyOnExecute;
    protected override string Read() => GlobalConstants.MainConfig!.Data.CurrentFloatingWindowProfile;
    protected override string Target()
    {
        var names = Service.Profiles.GetProfileNames();
        var name = Settings.TargetProfileName ?? names[(Math.Max(0, names.ToList().IndexOf(Read())) + 1) % names.Count];
        if (!Service.Profiles.ProfileFileExists(name)) throw new FileNotFoundException("方案不存在，请重新选择。", name);
        return name;
    }
    protected override void Write(string value) => Service.SwitchProfile(value);
}

public sealed class NativeShowFloatingSettingsControl : ActionSettingsControlBase<ShowFloatingWindowSettings>
{
    protected override void OnInitialized() { base.OnInitialized(); var value = new CheckBox { Content = "显示悬浮窗" }; value.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(Settings.ShowFloatingWindow)) { Source = Settings, Mode = BindingMode.TwoWay }); Content = value; }
}
public sealed class NativeFloatingLayerSettingsControl : ActionSettingsControlBase<ToggleFloatingWindowLayerSettings>
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        var choice = new ComboBox { ItemsSource = new[] { "切换置顶/置底", "置底（当前平台需支持）", "置顶" }, SelectedIndex = Settings.TargetLayer + 1 };
        choice.SelectionChanged += (_, _) => Settings.TargetLayer = choice.SelectedIndex - 1;
        Content = choice;
    }
}
public sealed class NativeFloatingThemeSettingsControl : ActionSettingsControlBase<SwitchFloatingWindowThemeSettings>
{
    protected override void OnInitialized()
    {
        base.OnInitialized(); var choice = new ComboBox { ItemsSource = new[] { "切换", "跟随", "浅色", "深色", "自适应背景" }, SelectedIndex = Settings.TargetTheme + 1 };
        choice.SelectionChanged += (_, _) => Settings.TargetTheme = choice.SelectedIndex - 1; Content = choice;
    }
}
public sealed class NativeFloatingProfileSettingsControl : ActionSettingsControlBase<ToggleFloatingWindowProfileSettings>
{
    protected override void OnInitialized()
    {
        base.OnInitialized(); var name = new TextBox { PlaceholderText = "留空切换到下一方案" };
        name.Bind(TextBox.TextProperty, new Binding(nameof(Settings.TargetProfileName)) { Source = Settings, Mode = BindingMode.TwoWay }); Content = name;
    }
}
