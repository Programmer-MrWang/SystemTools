using System.Reflection;
using System.Runtime.Loader;
using SystemTools.Contracts;

namespace SystemTools;

internal static class BackendLoader
{
    public static IPlatformBackend Load(string pluginDirectory, string runtimeId)
    {
        var module = PlatformSelection.ModuleName(runtimeId);
        var path = Path.GetFullPath(Path.Combine(pluginDirectory, module + ".dll"));
        if (!File.Exists(path))
            throw new FileNotFoundException($"缺少当前平台模块 {module}；请安装完整 M1 包。", path);
        var context = AssemblyLoadContext.GetLoadContext(typeof(Plugin).Assembly)
                      ?? throw new InvalidOperationException("入口没有程序集加载上下文。");
        // Host's macOS resolver only probes the plugin root. Explicit paths also
        // avoid merging three incompatible deps.json files for CoreCLR hosts.
        var assembly = context.LoadFromAssemblyPath(path);
        var type = assembly.GetType(module + ".Backend", throwOnError: true)!;
        if (Activator.CreateInstance(type) is not IPlatformBackend backend ||
            backend.Describe().RuntimeId != runtimeId)
            throw new InvalidOperationException($"平台模块身份不匹配：{path}");
        return backend;
    }
}
