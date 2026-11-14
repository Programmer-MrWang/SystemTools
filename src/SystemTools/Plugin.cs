using ClassIsland.Core;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ClassIsland.Shared;
using SystemTools.Contracts;

namespace SystemTools;

[PluginEntrance]
public sealed class Plugin : PluginBase
{
    private int _initialized;
    private int _started;
    private int _stopped;
    private readonly SystemTools.Common.Lifecycle.LifetimeScope _lifetime = new();
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0) return;
        IPlatformBackend backend;
        try
        {
            var runtimeId = PlatformSelection.Select(PlatformEnvironment.Current());
            backend = BackendLoader.Load(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!, runtimeId);
        }
        catch (Exception ex)
        {
            if (Environment.GetEnvironmentVariable("SystemTools_M1_SmokeTest") == "1")
                File.WriteAllText(Path.Combine(PluginConfigFolder, "M1.LoadFailure.json"),
                    System.Text.Json.JsonSerializer.Serialize(new { passed = false, error = ex.ToString() }));
            throw;
        }
        var catalog = new FeatureCatalog(backend.Describe().RuntimeId);
        services.AddSingleton(catalog);
        if (backend is IPlatformPlugin platformPlugin)
            platformPlugin.Initialize(this, context, services);
        else
        {
            SharedHostRegistration.Initialize(PluginConfigFolder, Info.PluginFolderPath, services, catalog, _lifetime);
            if (backend is INativeDesktopBackend nativeBackend)
            {
                var native = nativeBackend.CreateServices();
                _lifetime.Own(native.Dispose);
                NativeHostRegistration.Initialize(services, catalog, native);
            }
            EventHandler startShared = (_, _) => SystemTools.Services.SuperAutoIslandIntegration.Register();
            AppBase.Current.AppStarted += startShared;
            _lifetime.Own(() => AppBase.Current.AppStarted -= startShared);
        }
        LegacyActionDiagnostics.Register(services);
        var diagnostics = new DiagnosticService(backend, PluginConfigFolder);
        services.AddSingleton(backend);
        services.AddSingleton(diagnostics);
        services.AddSettingsPage<DiagnosticsSettingsPage>();
        services.AddSingleton(new MigrationLocation(PluginConfigFolder));
        services.AddSettingsPage<MigrationSettingsPage>();
        services.AddAction<RecordDiagnosticsAction>();
        EventHandler started = async (_, _) =>
        {
            if (Interlocked.Exchange(ref _started, 1) != 0) return;
            try
            {
                diagnostics.Capture("host-started");
                if (Environment.GetEnvironmentVariable("SystemTools_M1_SmokeTest") == "1")
                    await diagnostics.RunHostSmokeAsync();
            }
            catch (Exception ex)
            {
                IAppHost.TryGetService<ILogger<Plugin>>()?.LogError(ex, "M1 诊断保存失败。");
            }
        };
        AppBase.Current.AppStarted += started;
        _lifetime.Own(() => AppBase.Current.AppStarted -= started);
        EventHandler? stopping = null;
        stopping = (_, _) =>
        {
            if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
            AppBase.Current.AppStopping -= stopping;
            foreach (var error in _lifetime.Close())
                IAppHost.TryGetService<ILogger<Plugin>>()?.LogWarning("取消插件事件订阅失败：{Type}", error.GetType().Name);
            if (backend is not IPlatformPlugin)
            {
                try { SystemTools.Shared.GlobalConstants.MainConfig?.Save(); }
                catch (Exception error) { IAppHost.TryGetService<ILogger<Plugin>>()?.LogWarning("保存配置失败，原文件已保留：{Type}", error.GetType().Name); }
                SystemTools.Shared.GlobalConstants.MainConfig?.Dispose();
            }
        };
        AppBase.Current.AppStopping += stopping;
    }
}
