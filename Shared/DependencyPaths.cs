using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace SystemTools.Shared;

public static class DependencyPaths
{
    private const string CacheFolderName = "Cache";
    private const string DependencyFolderName = "SystemTools";
    private static bool _initialized;
    private static readonly object SyncRoot = new();

    public static string GetDependencyRoot(string pluginFolder)
    {
        if (string.IsNullOrWhiteSpace(pluginFolder))
        {
            throw new ArgumentException("Plugin folder cannot be empty.", nameof(pluginFolder));
        }

        return Path.GetFullPath(Path.Combine(pluginFolder, "..", "..", CacheFolderName, DependencyFolderName));
    }

    public static string GetDependencyRoot() => GetDependencyRoot(GlobalConstants.Information.PluginFolder);

    public static string GetFfmpegPath() => Path.Combine(GetDependencyRoot(), "ffmpeg.exe");

    public static string? FindVoskModelDirectory()
    {
        var root = GetDependencyRoot();
        if (!Directory.Exists(root))
        {
            return null;
        }

        var preferredNames = new[]
        {
            "VoskModel",
            "vosk-model",
            "model",
            "vosk-model-small-cn-0.22",
            "vosk-model-cn"
        };
        foreach (var name in preferredNames)
        {
            var candidate = Path.Combine(root, name);
            if (IsVoskModelDirectory(candidate))
            {
                return candidate;
            }
        }

        return Directory.EnumerateDirectories(root)
            .FirstOrDefault(IsVoskModelDirectory);
    }

    public static string? FindVoskWorkerPath()
    {
        var rootCandidate = Path.Combine(
            GetDependencyRoot(),
            "VoskWorker",
            "SystemTools.VoskWorker.exe");
        if (IsVoskWorkerInstallation(rootCandidate))
        {
            return rootCandidate;
        }

        var modelDirectory = FindVoskModelDirectory();
        if (modelDirectory is null)
        {
            return null;
        }

        var modelCandidate = Path.Combine(
            modelDirectory,
            "VoskWorker",
            "SystemTools.VoskWorker.exe");
        if (IsVoskWorkerInstallation(modelCandidate))
        {
            return modelCandidate;
        }

        var pluginCandidate = Path.Combine(
            GlobalConstants.Information.PluginFolder,
            "VoskWorker",
            "SystemTools.VoskWorker.exe");
        return IsVoskWorkerInstallation(pluginCandidate) ? pluginCandidate : null;
    }

    public static (bool IsAvailable, string Message) CheckVoskDependencies()
    {
        try
        {
            var model = FindVoskModelDirectory();
            if (model is null)
            {
                return (false, $"找不到完整的 Vosk 语音识别模型。请将模型放入 {GetDependencyRoot()} 下。");
            }

            if (FindVoskWorkerPath() is null)
            {
                return (false, $"找不到 Vosk 工作进程。请确认 VoskWorker 文件夹位于 {GetDependencyRoot()} 或插件目录下。");
            }

            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, $"检查 Vosk 依赖失败：{ex.Message}");
        }
    }

    private static bool IsVoskModelDirectory(string path)
    {
        return Directory.Exists(path) &&
               File.Exists(Path.Combine(path, "conf", "mfcc.conf")) &&
               File.Exists(Path.Combine(path, "am", "final.mdl")) &&
               File.Exists(Path.Combine(path, "graph", "HCLG.fst")) &&
               File.Exists(Path.Combine(path, "graph", "words.txt"));
    }

    private static bool IsVoskWorkerInstallation(string executablePath)
    {
        var directory = Path.GetDirectoryName(executablePath);
        return directory is not null &&
               File.Exists(executablePath) &&
               File.Exists(Path.Combine(directory, "SystemTools.VoskWorker.dll")) &&
               File.Exists(Path.Combine(directory, "Vosk.dll")) &&
               File.Exists(Path.Combine(directory, "libvosk.dll")) &&
               File.Exists(Path.Combine(directory, "hostfxr.dll")) &&
               File.Exists(Path.Combine(directory, "coreclr.dll"));
    }

    public static string GetFaceModelsDirectory() => Path.Combine(GetDependencyRoot(), "Models");

    public static string GetDependencyFile(string fileName) => Path.Combine(GetDependencyRoot(), fileName);

    public static bool HasFfmpegDependency()
    {
        try
        {
            return File.Exists(GetFfmpegPath());
        }
        catch
        {
            return false;
        }
    }

    public static bool HasFaceRecognitionDependencies()
    {
        try
        {
            return GetFaceRecognitionRequiredPaths().All(path =>
                path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
                    ? File.Exists(path)
                    : Directory.Exists(path));
        }
        catch
        {
            return false;
        }
    }

    public static string[] GetFaceRecognitionRequiredPaths()
    {
        var dependencyRoot = GetDependencyRoot();
        return
        [
            GetFaceModelsDirectory(),
            Path.Combine(GetFaceModelsDirectory(), "shape_predictor_68_face_landmarks.dat"),
            Path.Combine(GetFaceModelsDirectory(), "dlib_face_recognition_resnet_model_v1.dat"),
            Path.Combine(dependencyRoot, "runtimes"),
            GetDependencyFile("OpenCvSharp.Extensions.dll"),
            GetDependencyFile("OpenCvSharp.dll"),
            GetDependencyFile("DlibDotNet.dll")
        ];
    }

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

        foreach (var directory in Enumerable.Reverse(directories))
        {
            pathEntries.RemoveAll(x => string.Equals(x, directory, StringComparison.OrdinalIgnoreCase));
            pathEntries.Insert(0, directory);
        }

        Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator, pathEntries));
    }
}
