// Only the registry needed by the unmodified host loader snapshot.
namespace ClassIsland.Services;
public static class PluginService
{
    public static Dictionary<string, ClassIsland.PluginLoadContext> PluginLoadContexts { get; } = [];
}
