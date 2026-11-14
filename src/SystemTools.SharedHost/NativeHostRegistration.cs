using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.Core.Models.Automation;
using ClassIsland.Core.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using SystemTools.Actions;
using SystemTools.Contracts;
using SystemTools.Controls;
using SystemTools.Shared;
using ClassIsland.Shared;
using ClassIsland.Core.Models.Notification;
using SystemTools.Services;

namespace SystemTools;

public static class NativeHostRegistration
{
    public static void Initialize(IServiceCollection services, FeatureCatalog catalog, NativeDesktopServices native)
    {
        services.AddSingleton(native);
        if (native.Volume is not null)
        {
            services.AddSingleton(native.Volume);
            Bind(catalog, "SystemTools.SetVolume", native.Volume);
            if (catalog.CanCreate("SystemTools.SetVolume")) services.AddAction<NativeSetVolumeAction, SetVolumeSettingsControl>();
        }
        if (native.Idle is not null)
        {
            services.AddSingleton(native.Idle);
            catalog.BindForRegistration("SystemTools.LongIdleTrigger", () => GlobalConstants.MainConfig!.Data.IsTriggerEnabled("SystemTools.LongIdleTrigger") ? native.Idle.Probe() : new(CapabilityState.Disabled, "用户已关闭。"));
            if (catalog.CanCreate("SystemTools.LongIdleTrigger")) services.AddTrigger<NativeIdleTrigger, NativeIdleSettingsControl>();
        }
        services.AddSettingsPage<NativePermissionSettingsPage>();
        if (native.ScreenCapture is not null)
        {
            services.AddSingleton(native.ScreenCapture);
            Bind(catalog, "SystemTools.ScreenShot", native.ScreenCapture);
            if (catalog.CanCreate("SystemTools.ScreenShot")) services.AddAction<NativeScreenShotAction, ScreenShotSettingsControl>();
        }
        if (native.UsbDevices is not null)
        {
            services.AddSingleton(native.UsbDevices);
            catalog.BindForRegistration("SystemTools.UsbDeviceTrigger", () => GlobalConstants.MainConfig!.Data.IsTriggerEnabled("SystemTools.UsbDeviceTrigger") ? native.UsbDevices.Probe() : new(CapabilityState.Disabled, "用户已关闭。"));
            if (catalog.CanCreate("SystemTools.UsbDeviceTrigger")) services.AddTrigger<NativeUsbTrigger, NativeUsbSettingsControl>();
            Bind(catalog, "SystemTools.AutoOpenUsbDriveOnInsert", native.UsbDevices);
            if (catalog.CanCreate("SystemTools.AutoOpenUsbDriveOnInsert")) services.AddAction<NativeUsbAutoPlayAction, NativeUsbAutoPlaySettingsControl>();
            services.AddHostedService<NativeUsbAutoPlayService>();
        }
        if (native.Keyboard is not null)
        {
            services.AddSingleton(native.Keyboard);
            foreach (var id in new[] { "SimulateKeyboard", "SimulateKeyCombination", "EnterKey", "EscKey", "F11Key" }) Bind(catalog, "SystemTools." + id, native.Keyboard);
            if (catalog.CanCreate("SystemTools.SimulateKeyboard")) services.AddAction<NativeKeyboardAction, NativeKeyboardSettingsControl>();
            if (catalog.CanCreate("SystemTools.SimulateKeyCombination")) services.AddAction<NativeCombinationAction, NativeCombinationSettingsControl>();
            if (catalog.CanCreate("SystemTools.EnterKey")) services.AddAction<NativeEnterAction, ShortcutKeyNotificationSettingsControl>();
            if (catalog.CanCreate("SystemTools.EscKey")) services.AddAction<NativeEscapeAction, ShortcutKeyNotificationSettingsControl>();
            if (catalog.CanCreate("SystemTools.F11Key")) services.AddAction<NativeF11Action, ShortcutKeyNotificationSettingsControl>();
        }
        if (native.Hotkeys is not null)
        {
            services.AddSingleton(native.Hotkeys);
            catalog.BindForRegistration("SystemTools.HotkeyTrigger", () => GlobalConstants.MainConfig!.Data.IsTriggerEnabled("SystemTools.HotkeyTrigger") ? native.Hotkeys.Probe() : new(CapabilityState.Disabled, "用户已关闭。"));
            if (catalog.CanCreate("SystemTools.HotkeyTrigger")) services.AddTrigger<NativeHotkeyTrigger, NativeHotkeySettingsControl>();
        }
        if (native.Windows is not null)
        {
            services.AddSingleton(native.Windows);
            Bind(catalog, "SystemTools.WindowOperation", native.Windows);
            if (catalog.CanCreate("SystemTools.WindowOperation")) services.AddAction<NativeWindowOperationAction, WindowOperationSettingsControl>();
        }
        services.AddSingleton<NativeFloatingWindowService>();
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(provider => provider.GetRequiredService<NativeFloatingWindowService>());
        foreach (var suffix in new[] { "ShowFloatingWindow", "ToggleFloatingWindowLayer", "SwitchFloatingWindowTheme", "ToggleFloatingWindowProfile" })
            catalog.BindForRegistration("SystemTools." + suffix, () => GlobalConstants.MainConfig!.Data.EnableFloatingWindowFeature && GlobalConstants.MainConfig.Data.IsActionEnabled("SystemTools." + suffix) ? new(CapabilityState.Available) : new(CapabilityState.Disabled, "请启用悬浮窗功能与对应行动。"));
        catalog.BindForRegistration("SystemTools.FloatingWindowTrigger", () => GlobalConstants.MainConfig!.Data.EnableFloatingWindowFeature && GlobalConstants.MainConfig.Data.IsTriggerEnabled("SystemTools.FloatingWindowTrigger") ? new(CapabilityState.Available) : new(CapabilityState.Disabled, "请启用悬浮窗功能。"));
        if (catalog.CanCreate("SystemTools.FloatingWindowTrigger")) services.AddTrigger<NativeFloatingTrigger, NativeFloatingTriggerSettingsControl>();
        if (catalog.CanCreate("SystemTools.ShowFloatingWindow")) services.AddAction<NativeShowFloatingAction, NativeShowFloatingSettingsControl>();
        if (catalog.CanCreate("SystemTools.ToggleFloatingWindowLayer")) services.AddAction<NativeFloatingLayerAction, NativeFloatingLayerSettingsControl>();
        if (catalog.CanCreate("SystemTools.SwitchFloatingWindowTheme")) services.AddAction<NativeFloatingThemeAction, NativeFloatingThemeSettingsControl>();
        if (catalog.CanCreate("SystemTools.ToggleFloatingWindowProfile")) services.AddAction<NativeFloatingProfileAction, NativeFloatingProfileSettingsControl>();
        var group = new ActionMenuTreeGroup("系统原生功能", "\uE079");
        foreach (var status in catalog.Snapshot().Where(x => x.Feature.Kind == "action" && x.Capability.CanExecute &&
                     x.Feature.Id is "SystemTools.SetVolume" or "SystemTools.ScreenShot" or "SystemTools.AutoOpenUsbDriveOnInsert" or "SystemTools.SimulateKeyboard" or "SystemTools.SimulateKeyCombination" or "SystemTools.EnterKey" or "SystemTools.EscKey" or "SystemTools.F11Key" or "SystemTools.WindowOperation" or "SystemTools.ShowFloatingWindow" or "SystemTools.ToggleFloatingWindowLayer" or "SystemTools.SwitchFloatingWindowTheme" or "SystemTools.ToggleFloatingWindowProfile"))
            group.Children.Add(new ActionMenuTreeItem(status.Feature.Id, status.Feature.Name, "\uE079"));
        if (group.Children.Count > 0) IActionService.ActionMenuTree.Add(group);
    }

    internal static void Bind(FeatureCatalog catalog, string id, INativeCapability native)
    {
        catalog.BindForRegistration(id, () =>
            GlobalConstants.MainConfig!.Data.IsActionEnabled(id) ? native.Probe() : new(CapabilityState.Disabled, "用户已关闭。"));
    }
}

[ActionInfo("SystemTools.SetVolume", "设置系统音量", "\uF013", false)]
public sealed class NativeSetVolumeAction(ISystemVolume volume, FeatureCatalog catalog) : ActionBase<SetVolumeSettings>
{
    protected override async Task OnInvoke()
    {
        catalog.EnsureExecutable(ActionItem.Id);
        var result = await volume.SetAsync(Settings.VolumePercent, InterruptCancellationToken);
        if (Math.Abs(result.Percent - Settings.VolumePercent) > 2)
            throw new InvalidOperationException("音量调整后的读回值不符，设备可能已切换，请重试。");
        NativeActionFeedback.Notify(Settings.NotifyOnExecute, "已调整系统音量");
    }
}

internal static class NativeActionFeedback
{
    internal static void Notify(bool enabled, string message)
    {
        if (enabled) IAppHost.GetService<SystemToolsNotificationProvider>().ShowNotification(new NotificationRequest
        {
            MaskContent = NotificationContent.CreateTwoIconsMask(message, "\uE9FB", "")
        });
    }
}
