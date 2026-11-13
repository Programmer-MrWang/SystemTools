using ClassIsland.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SystemTools.Contracts;

public interface IPlatformPlugin
{
    void Initialize(PluginBase entrance, HostBuilderContext context, IServiceCollection services);
}
