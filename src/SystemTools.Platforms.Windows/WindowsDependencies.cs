using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace SystemTools.Platforms.Windows;

internal static class WindowsDependencies
{
    private static int _installed;
    public static void Install()
    {
        if (System.Threading.Interlocked.Exchange(ref _installed, 1) != 0) return;
        var assembly = typeof(Backend).Assembly;
        var context = AssemblyLoadContext.GetLoadContext(assembly)!;
        var directory = Path.GetDirectoryName(assembly.Location)!;
        var privateDirectory = Path.Combine(directory, "platforms", "win-x64");
        context.Resolving += (_, name) =>
        {
            var privatePath = Path.Combine(privateDirectory, name.Name + ".dll");
            if (File.Exists(privatePath))
                return context.LoadFromAssemblyPath(privatePath);
            var path = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };
    }
}

