using System.Reflection;
using System.Runtime.Loader;

namespace SystemTools;

internal static class SharedDependencies
{
    private static int _installed;
    internal static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0) return;
        var assembly = typeof(SharedDependencies).Assembly;
        var context = AssemblyLoadContext.GetLoadContext(assembly)!;
        var root = Path.GetDirectoryName(assembly.Location)!;
        context.Resolving += (_, name) =>
        {
            var path = Path.Combine(root, "platforms", "shared", name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };
    }
}
