using System.IO;
using System.Text;
using ClassIsland.Shared.Helpers;
using SystemTools.Common.Storage;

namespace SystemTools.Shared;

internal static class PluginJsonStore
{
    internal static AtomicJsonStore<T> Open<T>(string path) where T : class, new() => new(path, Serialize, Deserialize<T>);
    private static string Serialize<T>(T value)
    {
        using var stream = new MemoryStream();
        ConfigureFileHelper.SaveConfig(stream, value);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    private static T Deserialize<T>(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return ConfigureFileHelper.LoadConfigUnWrapped<T>(stream);
    }
}
