using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Data;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Shared.Enums;
using SystemTools.Triggers;

namespace SystemTools;

[TriggerInfo("SystemTools.FloatingWindowTrigger", "从悬浮窗触发", "\uEA37")]
public sealed class NativeFloatingTrigger(NativeFloatingWindowService service) : TriggerBase<FloatingWindowTriggerConfig>
{
    internal bool IsLoaded { get; private set; }
    internal FloatingWindowTriggerConfig Configuration => Settings;
    internal bool IsWaitingForRevert => AssociatedWorkflow?.ActionSet is { IsRevertEnabled: true, Status: ActionSetStatus.IsOn };
    public override void Loaded()
    {
        UnLoaded(); IsLoaded = true;
        Settings.PropertyChanged += Changed;
        if (AssociatedWorkflow?.ActionSet is { } set) set.PropertyChanged += Changed;
        service.Register(this);
    }
    public override void UnLoaded()
    {
        IsLoaded = false;
        Settings.PropertyChanged -= Changed;
        if (AssociatedWorkflow?.ActionSet is { } set) set.PropertyChanged -= Changed;
        service.Unregister(this);
    }
    private void Changed(object? sender, PropertyChangedEventArgs args) => service.QueueRefresh();
    internal void InvokeFromWindow() { if (!IsLoaded) return; if (IsWaitingForRevert) TriggerRevert(); else Trigger(); }
}

public sealed class NativeFloatingTriggerSettingsControl : TriggerSettingsControlBase<FloatingWindowTriggerConfig>
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        var name = new TextBox { PlaceholderText = "按钮名称" };
        name.Bind(TextBox.TextProperty, new Binding(nameof(Settings.ButtonName)) { Source = Settings, Mode = BindingMode.TwoWay });
        var visible = new CheckBox { Content = "显示此按钮" };
        visible.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(Settings.IsVisible)) { Source = Settings, Mode = BindingMode.TwoWay });
        Content = new StackPanel { Spacing = 8, Children = { name, visible } };
    }
}
