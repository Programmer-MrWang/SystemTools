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
        var group = new ActionMenuTreeGroup("系统原生功能", "\uE079");
        foreach (var status in catalog.Snapshot().Where(x => x.Feature.Kind == "action" && x.Capability.CanExecute &&
                     x.Feature.Id is "SystemTools.SetVolume" or "SystemTools.ScreenShot" or "SystemTools.AutoOpenUsbDriveOnInsert"))
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
