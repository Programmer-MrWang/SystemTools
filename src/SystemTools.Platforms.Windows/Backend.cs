using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Extensions.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SystemTools.Contracts;
namespace SystemTools.Platforms.Windows;
public sealed class Backend : IPlatformBackend, IPlatformPlugin
{
    public PlatformDescriptor Describe() => new("win-x64", "Windows x64", "M2 Windows 业务接入");

    public void Initialize(PluginBase entrance, HostBuilderContext context, IServiceCollection services)
    {
        WindowsDependencies.Install();
        new global::SystemTools.Plugin().InitializeFromEntrance(entrance, context, services);
        if (!ClassIsland.Core.Abstractions.Services.IActionService.ActionInfos.ContainsKey("SystemTools.CameraCapture") &&
            Shared.GlobalConstants.MainConfig!.Data.IsActionEnabled("SystemTools.CameraCapture"))
            services.AddAction<UnavailableCameraAction>();
        if (System.Environment.GetEnvironmentVariable("SystemTools_M2_Acceptance") == "1")
            ClassIsland.Core.AppBase.Current.AppStarted += async (_, _) => await WindowsAcceptance.RunAsync(6);
    }
}
