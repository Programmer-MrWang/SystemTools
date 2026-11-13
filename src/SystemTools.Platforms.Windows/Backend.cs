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
        services.AddAction<OpenSettingsAction>();
    }
}
