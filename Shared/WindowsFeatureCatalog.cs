using System;
using SystemTools.Contracts;
using SystemTools.ConfigHandlers;
using SystemTools.Shared;

namespace SystemTools.Shared;

internal static class WindowsFeatureCatalog
{
    internal static void Bind(FeatureCatalog catalog, MainConfigData config)
    {
        foreach (var feature in catalog.Snapshot())
        {
            var definition = feature.Feature;
            catalog.BindForRegistration(definition.Id, () => Probe(definition, config));
        }
    }
    private static Capability Probe(FeatureDefinition feature, MainConfigData config)
    {
        if (feature.Id is "SystemTools.FullscreenClock" or "SystemTools.ClickSimulation")
            return new(CapabilityState.Unsupported, "统一版本不提供此入口，保留旧配置。" );
        var enabled = feature.Kind switch
        {
            "action" => config.IsActionEnabled(feature.Id),
            "trigger" => config.IsTriggerEnabled(feature.Id),
            "component" => config.IsComponentEnabled(feature.Id),
            "rule" => config.IsRuleEnabled(feature.Id),
            "data" => true,
            _ => false
        };
        if (!enabled) return new(CapabilityState.Disabled, "用户已关闭该功能。" );
        if (feature.Id is "SystemTools.DisableMouse" or "SystemTools.EnableMouse" && !config.EnableExperimentalFeatures)
            return new(CapabilityState.Disabled, "实验性功能未启用。" );
        if (feature.Id is "SystemTools.EnableVoiceWakeAi" or "SystemTools.WakeUpVoiceConversationAi" or "SystemTools.ShowAiChatDialog" && !config.EnableAiService)
            return new(CapabilityState.Disabled, "AI 服务未启用。" );
        if (feature.Id is "SystemTools.ShowFloatingWindow" or "SystemTools.ToggleFloatingWindowLayer" or "SystemTools.ToggleFloatingWindowProfile" or "SystemTools.SwitchFloatingWindowTheme" && !config.EnableFloatingWindowFeature)
            return new(CapabilityState.Disabled, "悬浮窗功能未启用。" );
        if (feature.Id == "SystemTools.CameraCapture")
        {
            if (!config.EnableFfmpegFeatures) return new(CapabilityState.Disabled, "摄像头功能未启用。" );
            if (!DependencyPaths.HasFfmpegDependency()) return new(CapabilityState.MissingDependency, "缺少 FFmpeg，请在设置中安装依赖。" );
        }
        return new(CapabilityState.Available, "已接入实现；设备与权限由执行时检查。" );
    }
}
