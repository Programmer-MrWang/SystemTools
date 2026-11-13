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
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
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
        if (backend is IPlatformPlugin platformPlugin)
            platformPlugin.Initialize(this, context, services);
        var diagnostics = new DiagnosticService(backend, PluginConfigFolder);
        services.AddSingleton(backend);
        services.AddSingleton(diagnostics);
        services.AddSettingsPage<DiagnosticsSettingsPage>();
        services.AddAction<RecordDiagnosticsAction>();
        AppBase.Current.AppStarted += async (_, _) =>
        {
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
    }
}
