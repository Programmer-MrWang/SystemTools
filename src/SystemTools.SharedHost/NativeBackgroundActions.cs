using Avalonia.Controls;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using SystemTools.ConfigHandlers;
using SystemTools.Contracts;

namespace SystemTools;

public sealed class NativeBackgroundSettings
{
    [System.Text.Json.Serialization.JsonPropertyName("enable")] public bool Enable { get; set; } = true;
    [System.Text.Json.Serialization.JsonPropertyName("notifyOnExecute")] public bool NotifyOnExecute { get; set; }
}

[ActionInfo("SystemTools.AutoSwitchClassIslandTheme", "自动切换 ClassIsland 主题", "\uE5CB", false)]
public sealed class NativeBackgroundThemeAction(MainConfigHandler config, FeatureCatalog catalog) : ActionBase<NativeBackgroundSettings>
{
    protected override Task OnInvoke()
    {
        if (Settings.Enable) catalog.EnsureExecutable(ActionItem.Id);
        config.Data.AutoSwitchClassIslandTheme = Settings.Enable; config.Save();
        NativeActionFeedback.Notify(Settings.NotifyOnExecute, Settings.Enable ? "已开启背景主题" : "已关闭背景主题");
        return Task.CompletedTask;
    }
}
public sealed class NativeBackgroundSettingsControl : ActionSettingsControlBase<NativeBackgroundSettings>
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        var enabled = new CheckBox { Content = "启用", IsChecked = Settings.Enable };
        enabled.IsCheckedChanged += (_, _) => Settings.Enable = enabled.IsChecked == true;
        var notify = new CheckBox { Content = "执行时通知", IsChecked = Settings.NotifyOnExecute };
        notify.IsCheckedChanged += (_, _) => Settings.NotifyOnExecute = notify.IsChecked == true;
        Content = new StackPanel { Spacing = 8, Children = { enabled, notify } };
    }
}
