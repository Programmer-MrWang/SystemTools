using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.Core.Models.Automation;
using ClassIsland.Core.Models.XamlTheme;
using Microsoft.Extensions.DependencyInjection;
using SystemTools.Actions;
using SystemTools.Common.Files;
using SystemTools.ConfigHandlers;
using SystemTools.Contracts;
using SystemTools.Controls;
using SystemTools.Controls.Components;
using SystemTools.Models.ComponentSettings;
using SystemTools.Rules;
using SystemTools.Services;
using SystemTools.Shared;

namespace SystemTools;

public static class SharedHostRegistration
{
    public static void Initialize(string configFolder, string pluginFolder, IServiceCollection services, FeatureCatalog catalog)
    {
        GlobalConstants.IsUnifiedBuild = true;
        GlobalConstants.PluginConfigFolder = configFolder;
        GlobalConstants.Information.PluginFolder = pluginFolder;
        GlobalConstants.Features = catalog;
        CredentialProtection.Provider = new UnavailableCredentialProtector("unavailable", "本平台系统凭据后端尚未接入。原密文与授权配置已保留。" );
        var config = new MainConfigHandler(configFolder);
        GlobalConstants.MainConfig = config;
        services.AddSingleton(config);
        services.AddSingleton<FileOperations>();
        services.AddSingleton<FloatingWindowProfileManager>();
        services.AddSingleton<ClassIslandProfileExcelExporter>();
        services.AddSingleton<IOpenAiCompatibleService, OpenAiCompatibleService>();
        services.AddSingleton<AiConversationStore>();
        services.AddSingleton<AiChatOperationGate>();
        services.AddSingleton<AiPromptService>();
        services.AddSingleton<ClassIslandActionAiService>();
        services.AddSingleton<ClassIslandProfileAiService>();
        services.AddNotificationProvider<SystemToolsNotificationProvider>();
        var hosted = services.Single(x => x.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService) && x.ImplementationType == typeof(SystemToolsNotificationProvider));
        services.Remove(hosted);
        services.AddSingleton<SystemToolsNotificationProvider>();
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(provider => provider.GetRequiredService<SystemToolsNotificationProvider>());
        string[] actions = ["Copy", "Move", "Delete", "ToggleWorkflow", "LoadTemporaryClassPlan", "ClearAllNotifications", "OpenAppSettings", "OpenProfileEditor", "OpenClassSwapWindow", "TriggerCustomTrigger"];
        foreach (var suffix in actions)
        {
            var id = "SystemTools." + suffix;
            catalog.BindForRegistration(id, () => config.Data.IsActionEnabled(id) ? new(CapabilityState.Available, "共享实现已接入，尚待本平台运行验证。" ) : new(CapabilityState.Disabled, "用户已关闭。" ));
        }
        catalog.BindForRegistration("SystemTools.ReadFile", () => new(CapabilityState.Available, "共享实现已接入。" ));
        if (catalog.CanCreate("SystemTools.Copy")) services.AddAction<CopyAction, CopySettingsControl>();
        if (catalog.CanCreate("SystemTools.Move")) services.AddAction<MoveAction, MoveSettingsControl>();
        if (catalog.CanCreate("SystemTools.Delete")) services.AddAction<DeleteAction, DeleteSettingsControl>();
        if (catalog.CanCreate("SystemTools.ToggleWorkflow")) services.AddAction<ToggleWorkflowAction, ToggleWorkflowSettingsControl>();
        if (catalog.CanCreate("SystemTools.LoadTemporaryClassPlan")) services.AddAction<LoadTemporaryClassPlanAction, LoadTemporaryClassPlanSettingsControl>();
        if (catalog.CanCreate("SystemTools.ClearAllNotifications")) services.AddAction<ClearAllNotificationsAction, ShortcutKeyNotificationSettingsControl>();
        if (catalog.CanCreate("SystemTools.OpenAppSettings")) services.AddAction<OpenAppSettingsAction, ShortcutKeyNotificationSettingsControl>();
        if (catalog.CanCreate("SystemTools.OpenProfileEditor")) services.AddAction<OpenProfileEditorAction, ShortcutKeyNotificationSettingsControl>();
        if (catalog.CanCreate("SystemTools.OpenClassSwapWindow")) services.AddAction<OpenClassSwapWindowAction, ShortcutKeyNotificationSettingsControl>();
        if (catalog.CanCreate("SystemTools.TriggerCustomTrigger")) services.AddAction<TriggerCustomTriggerAction, TriggerCustomTriggerSettingsControl>();
        var group = new ActionMenuTreeGroup("SystemTools 行动", "\uE079");
        foreach (var status in catalog.Snapshot().Where(x => x.Feature.Kind == "action" && x.Capability.CanExecute))
            group.Children.Add(new ActionMenuTreeItem(status.Feature.Id, status.Feature.Name, "\uE079"));
        IActionService.ActionMenuTree.Add(group);
        foreach (var status in catalog.Snapshot().Where(x => x.Feature.Kind == "component"))
        {
            var id = status.Feature.Id;
            if (id == "SystemTools.LyricsDisplay") continue;
            catalog.BindForRegistration(id, () => config.Data.IsComponentEnabled(id) ? new(CapabilityState.Available, "共享组件已接入，尚待本平台运行验证。" ) : new(CapabilityState.Disabled, "用户已关闭。" ));
        }
        if (catalog.CanCreate("SystemTools.NetworkStatus")) services.AddComponent<NetworkStatusComponent, NetworkStatusSettingsControl>();
        if (catalog.CanCreate("SystemTools.ClipboardContent")) services.AddComponent<ClipboardContentComponent, ClipboardContentSettingsControl>();
        if (catalog.CanCreate("SystemTools.LocalQuote")) services.AddComponent<LocalQuoteComponent, LocalQuoteSettingsControl>();
        if (catalog.CanCreate("SystemTools.NextClassDisplay")) services.AddComponent<NextClassDisplayComponent, NextClassDisplaySettingsControl>();
        if (catalog.CanCreate("SystemTools.BetterCarouselContainer")) services.AddComponent<BetterCarouselContainerComponent, BetterCarouselContainerSettingsControl>();
        if (catalog.CanCreate("SystemTools.ScrollingText")) services.AddComponent<ScrollingTextComponent, ScrollingTextSettingsControl>();
        foreach (var suffix in new[] { "ProcessRunning", "UsingClassPlan", "UsingTimeLayout", "InTimePeriod" })
        {
            var id = "SystemTools." + suffix + "Rule";
            catalog.BindForRegistration(id, () => config.Data.IsRuleEnabled(id) ? new(CapabilityState.Available) : new(CapabilityState.Disabled, "用户已关闭。" ));
        }
        if (catalog.CanCreate("SystemTools.ProcessRunningRule")) services.AddRule<ProcessRunningRuleSettings, ProcessRunningRuleSettingsControl>("SystemTools.ProcessRunningRule", "程序正在运行", "\uE342", SharedRuleHandlers.HandleProcessRunningRule);
        if (catalog.CanCreate("SystemTools.UsingClassPlanRule")) services.AddRule<UsingClassPlanRuleSettings, UsingClassPlanRuleSettingsControl>("SystemTools.UsingClassPlanRule", "正在使用某课程表", "\uE6B1", SharedRuleHandlers.HandleUsingClassPlanRule);
        if (catalog.CanCreate("SystemTools.UsingTimeLayoutRule")) services.AddRule<UsingTimeLayoutRuleSettings, UsingTimeLayoutRuleSettingsControl>("SystemTools.UsingTimeLayoutRule", "正在使用某时间表", "\uE69D", SharedRuleHandlers.HandleUsingTimeLayoutRule);
        if (catalog.CanCreate("SystemTools.InTimePeriodRule")) services.AddRule<InTimePeriodRuleSettings, InTimePeriodRuleSettingsControl>("SystemTools.InTimePeriodRule", "是否在某时间段", "\uE4CA", SharedRuleHandlers.HandleInTimePeriodRule);
        foreach (var theme in new[] { ("CardTypeComponent", "Card-type-component"), ("ClassWidgets", "classwidgets"), ("NotchStyle", "notch-style") })
            services.AddXamlTheme(new Uri($"avares://SystemTools/Themes/{theme.Item1}/Styles.axaml"), new ThemeManifest { Id = theme.Item2, Name = theme.Item1, Version = "1.0.0.0", Author = "Programmer-MrWang" });
    }
}
