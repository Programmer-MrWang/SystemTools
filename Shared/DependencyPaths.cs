using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace SystemTools.Shared;

public static class DependencyPaths
{
    private const string ExtensionsFolderName = "Extensions";
    private const string DependencyFolderName = "SystemTools";
    private static bool _initialized;
    private static readonly object SyncRoot = new();

    public static string GetDependencyRoot(string pluginFolder)
    {
        if (string.IsNullOrWhiteSpace(pluginFolder))
        {
            throw new ArgumentException("Plugin folder cannot be empty.", nameof(pluginFolder));
        }

        return Path.GetFullPath(Path.Combine(pluginFolder, "..", "..", ExtensionsFolderName, DependencyFolderName));
    }

    public static string GetDependencyRoot() => GetDependencyRoot(GlobalConstants.Information.PluginFolder);

    public static string GetFfmpegPath() => Path.Combine(GetDependencyRoot(), "ffmpeg.exe");

    public static string GetFaceModelsDirectory() => Path.Combine(GetDependencyRoot(), "Models");

    public static string GetDependencyFile(string fileName) => Path.Combine(GetDependencyRoot(), fileName);

    public static void EnsureDependencyDirectories()
    {
        Directory.CreateDirectory(GetDependencyRoot());
    }

    public static void InitializeResolvers()
    {
        lock (SyncRoot)
        {
            if (_initialized)
            {
                return;
            }

            EnsureDependencyDirectories();

            var dependencyRoot = GetDependencyRoot();
            var searchDirectories = GetNativeSearchDirectories(dependencyRoot)
                .Where(Directory.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            PrependPathEnvironment(searchDirectories);
            AppDomain.CurrentDomain.AssemblyResolve += ResolveManagedAssembly;
            PreloadManagedAssemblies(dependencyRoot);
            _initialized = true;
        }
    }

    private static Assembly? ResolveManagedAssembly(object? sender, ResolveEventArgs args)
    {
        var assemblyName = new AssemblyName(args.Name).Name;
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return null;
        }

        var candidate = Path.Combine(GetDependencyRoot(), assemblyName + ".dll");
        if (!File.Exists(candidate))
        {
            return null;
        }

        return LoadAssembly(candidate);
    }

    private static void PreloadManagedAssemblies(string dependencyRoot)
    {
        foreach (var fileName in new[] { "OpenCvSharp.dll", "OpenCvSharp.Extensions.dll", "DlibDotNet.dll" })
        {
            var path = Path.Combine(dependencyRoot, fileName);
            if (File.Exists(path))
            {
                LoadAssembly(path);
            }
        }
    }

    private static Assembly LoadAssembly(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a =>
                   string.Equals(a.Location, fullPath, StringComparison.OrdinalIgnoreCase))
               ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
    }

    private static string[] GetNativeSearchDirectories(string dependencyRoot)
    {
        return new[]
        {
            dependencyRoot,
            Path.Combine(dependencyRoot, "runtimes"),
            Path.Combine(dependencyRoot, "runtimes", "win-x64", "native"),
            Path.Combine(dependencyRoot, "runtimes", "win-x86", "native"),
            Path.Combine(dependencyRoot, "runtimes", "win", "native")
        };
    }

    private static void PrependPathEnvironment(string[] directories)
    {
        if (directories.Length == 0)
        {
            return;
        }

        var current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathEntries = current.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();

        foreach (var directory in directories.Reverse())
        {
            pathEntries.RemoveAll(x => string.Equals(x, directory, StringComparison.OrdinalIgnoreCase));
            pathEntries.Insert(0, directory);
        }

        Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator, pathEntries));
    }
}
