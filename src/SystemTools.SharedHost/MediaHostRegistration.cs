using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.Core.Models.Automation;
using Microsoft.Extensions.DependencyInjection;
using SystemTools.Actions;
using SystemTools.Contracts;
using SystemTools.Controls;
using SystemTools.Shared;

namespace SystemTools;

public static class MediaHostRegistration
{
    public static MediaPreferencesStore RegisterCamera(IServiceCollection services, FeatureCatalog catalog, ICameraCapture camera,
        MediaPreferencesStore? preferences = null)
    {
        preferences ??= new(GlobalConstants.PluginConfigFolder!);
        services.AddSingleton(preferences);
        services.AddSingleton(camera);
        var context = new CameraCaptureContext(camera);
        services.AddSingleton(context);
        EventHandler? stopped = null;
        stopped = (_, _) => { AppBase.Current.AppStopping -= stopped; context.Dispose(); };
        AppBase.Current.AppStopping += stopped;
        services.AddSettingsPage<MediaSettingsPage>();
        catalog.BindForRegistration("SystemTools.CameraCapture", () =>
            !GlobalConstants.MainConfig!.Data.IsActionEnabled("SystemTools.CameraCapture") ||
            (catalog.RuntimeId == "win-x64" && !GlobalConstants.MainConfig.Data.EnableFfmpegFeatures)
                ? new(CapabilityState.Disabled, "请启用摄像头行动及媒体功能。") : camera.Probe());
        if (catalog.CanCreate("SystemTools.CameraCapture") && !IActionService.ActionInfos.ContainsKey("SystemTools.CameraCapture"))
        {
            services.AddAction<CameraCaptureAction, CameraCaptureSettingsControl>();
            var group = new ActionMenuTreeGroup("媒体功能", "\uE39E");
            group.Children.Add(new ActionMenuTreeItem("SystemTools.CameraCapture", "摄像头抓拍", "\uE39E"));
            IActionService.ActionMenuTree.Add(group);
        }
        return preferences;
    }
}
