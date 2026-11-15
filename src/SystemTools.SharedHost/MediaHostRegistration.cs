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
    public static void RegisterMedia(IServiceCollection services, FeatureCatalog catalog, PlatformMediaServices media)
    {
        services.AddSingleton(media);
        services.AddSingleton(media.Speech);
        services.AddSingleton(media.Authentication);
        services.AddSingleton(media.Media);
        services.AddSingleton<MediaPlaybackMonitor>();
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(provider => provider.GetRequiredService<MediaPlaybackMonitor>());
        services.AddSingleton<PortableSpeechService>();
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(provider => provider.GetRequiredService<PortableSpeechService>());
        if (catalog.RuntimeId != "win-x64")
        {
            var preferences = (MediaPreferencesStore)services.Last(item => item.ServiceType == typeof(MediaPreferencesStore)).ImplementationInstance!;
            catalog.BindForRegistration("SystemTools.MediaMusicPlayingRule", () => GlobalConstants.MainConfig!.Data.IsRuleEnabled("SystemTools.MediaMusicPlayingRule")
                ? media.Media.Probe() : new(CapabilityState.Disabled, "用户已关闭。"));
            if (catalog.CanCreate("SystemTools.MediaMusicPlayingRule"))
                services.AddRule<SystemTools.Rules.MediaMusicPlayingRuleSettings, NativeMediaRuleSettingsControl>("SystemTools.MediaMusicPlayingRule", "正在播放媒体音乐", "\uEDBF",
                    _ => ClassIsland.Shared.IAppHost.GetService<MediaPlaybackMonitor>().IsPlaying);
            catalog.BindForRegistration("SystemTools.LyricsDisplay", () => GlobalConstants.MainConfig!.Data.IsComponentEnabled("SystemTools.LyricsDisplay") &&
                File.Exists(preferences.Current.LyricsFile) ? media.Media.Probe() : new(CapabilityState.MissingDependency, "请先在媒体设置中选择当前曲目的 LRC 文件。"));
            if (catalog.CanCreate("SystemTools.LyricsDisplay")) services.AddComponent<NativeLyricsComponent, NativeLyricsSettingsControl>();
            if (preferences.Current.EnableSystemAuthentication && media.Authentication.Probe().CanExecute)
                services.AddAuthorizeProvider<NativeSystemAuthorizer>();
            catalog.BindForRegistration("SystemTools.KeywordTrigger", () => GlobalConstants.MainConfig!.Data.IsTriggerEnabled("SystemTools.KeywordTrigger")
                ? media.Speech.Probe() : new(CapabilityState.Disabled, "用户已关闭关键词触发。"));
            if (catalog.CanCreate("SystemTools.KeywordTrigger")) services.AddTrigger<NativeKeywordTrigger, NativeKeywordSettingsControl>();
            if (GlobalConstants.MainConfig!.Data.EnableAiService)
            {
                services.AddSingleton<NativeVoiceService>();
                services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(provider => provider.GetRequiredService<NativeVoiceService>());
                catalog.BindForRegistration("SystemTools.ShowAiChatDialog", () => GlobalConstants.MainConfig.Data.IsActionEnabled("SystemTools.ShowAiChatDialog")
                    ? new(CapabilityState.Available) : new(CapabilityState.Disabled, "用户已关闭。"));
                if (catalog.CanCreate("SystemTools.ShowAiChatDialog")) services.AddAction<NativeShowAiChatAction>();
                foreach (var id in new[] { "SystemTools.WakeUpVoiceConversationAi", "SystemTools.EnableVoiceWakeAi" })
                    catalog.BindForRegistration(id, () => GlobalConstants.MainConfig.Data.IsActionEnabled(id) ? media.Speech.Probe() : new(CapabilityState.Disabled, "用户已关闭。"));
                if (catalog.CanCreate("SystemTools.WakeUpVoiceConversationAi")) services.AddAction<NativeWakeConversationAction>();
                if (catalog.CanCreate("SystemTools.EnableVoiceWakeAi")) services.AddAction<NativeEnableVoiceWakeAction>();
                var group = new ActionMenuTreeGroup("语音与对话", "\uED53");
                foreach (var id in new[] { "SystemTools.ShowAiChatDialog", "SystemTools.WakeUpVoiceConversationAi", "SystemTools.EnableVoiceWakeAi" })
                    if (catalog.CanCreate(id)) group.Children.Add(new ActionMenuTreeItem(id, catalog.Snapshot().Single(item => item.Feature.Id == id).Feature.Name, "\uED53"));
                if (group.Children.Count > 0) IActionService.ActionMenuTree.Add(group);
            }
        }
        EventHandler? stopped = null;
        stopped = (_, _) => { AppBase.Current.AppStopping -= stopped; media.Dispose(); };
        AppBase.Current.AppStopping += stopped;
    }
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
