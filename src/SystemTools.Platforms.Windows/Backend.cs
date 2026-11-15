using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Extensions.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SystemTools.Contracts;
using SystemTools.Common.Media;
namespace SystemTools.Platforms.Windows;
public sealed class Backend : IPlatformBackend, IPlatformPlugin
{
    public PlatformDescriptor Describe() => new("win-x64", "Windows x64", "M2 Windows 业务接入");

    public void Initialize(PluginBase entrance, HostBuilderContext context, IServiceCollection services)
    {
        WindowsDependencies.Install();
        new global::SystemTools.Plugin().InitializeFromEntrance(entrance, context, services);
        var preferences = new global::SystemTools.MediaPreferencesStore(Shared.GlobalConstants.PluginConfigFolder!);
        var camera = new FfmpegCamera("win-x64", () =>
        {
            if (!string.IsNullOrWhiteSpace(preferences.Current.FfmpegPath))
                return MediaFiles.FindExecutable("ffmpeg", preferences.Current.FfmpegPath);
            var installed = Shared.DependencyPaths.GetFfmpegPath();
            return System.IO.File.Exists(installed) ? installed : MediaFiles.FindExecutable("ffmpeg");
        });
        global::SystemTools.MediaHostRegistration.RegisterCamera(services, Shared.GlobalConstants.Features!, camera, preferences);
        global::SystemTools.MediaHostRegistration.RegisterMedia(services, Shared.GlobalConstants.Features!, new PlatformMediaServices
        {
            Camera = camera, Speech = new WindowsSpeechRecognition(() => preferences.Current),
            Authentication = new WindowsSystemAuthentication(), Media = new WindowsMediaSessions()
        });
        if (!ClassIsland.Core.Abstractions.Services.IActionService.ActionInfos.ContainsKey("SystemTools.CameraCapture") &&
            Shared.GlobalConstants.MainConfig!.Data.IsActionEnabled("SystemTools.CameraCapture"))
            services.AddAction<UnavailableCameraAction>();
        if (System.Environment.GetEnvironmentVariable("SystemTools_M2_Acceptance") == "1")
            ClassIsland.Core.AppBase.Current.AppStarted += async (_, _) => await WindowsAcceptance.RunAsync(6);
    }
}
