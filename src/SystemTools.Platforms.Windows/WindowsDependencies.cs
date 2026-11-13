using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace SystemTools.Platforms.Windows;

internal static class WindowsDependencies
{
    public static void Install()
    {
        var assembly = typeof(Backend).Assembly;
        var context = AssemblyLoadContext.GetLoadContext(assembly)!;
        var directory = Path.GetDirectoryName(assembly.Location)!;
        context.Resolving += (_, name) =>
        {
            var path = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };
    }
}

