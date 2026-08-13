using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.InteropServices;

namespace SystemTools.Shared;

public enum SpeechRecognitionModelKind
{
    Unknown,
    Vosk,
    SenseVoice
}

public sealed record SpeechRecognitionModelInfo(
    string Directory,
    string? Name,
    SpeechRecognitionModelKind Kind);

public static class DependencyPaths
{
    private const string CacheFolderName = "Cache";
    private const string DependencyFolderName = "SystemTools";
    private const string SpeechModelMarkerFileName = "copyright.txt";
    private const string SpeechModelMarkerPrefix = "Officially certified by SystemTools";
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

    public static string? FindSpeechRecognitionModelDirectory()
    {
        var root = GetDependencyRoot();
        if (!Directory.Exists(root))
        {
            return null;
        }

        var preferredNames = new[]
        {
            "SenseVoiceSmall ONNX (INT8 Quantized)",
            "SenseVoiceModel",
            "sensevoice",
            "VoskModel",
            "vosk-model",
            "model",
            "vosk-model-small-en-us",
            "vosk-model-en-us",
            "vosk-model-small-cn-0.22",
            "vosk-model-small-cn",
            "vosk-model-cn"
        };
        foreach (var name in preferredNames)
        {
            var candidate = Path.Combine(root, name);
            if (IsSpeechRecognitionModelDirectory(candidate))
            {
                return candidate;
            }
        }

        return Directory.EnumerateDirectories(root)
            .FirstOrDefault(IsSpeechRecognitionModelDirectory);
    }

    public static string? FindSpeechRecognitionWorkerPath()
    {
        var rootCandidate = Path.Combine(
            GetDependencyRoot(),
            "VoskWorker",
            "SystemTools.VoskWorker.exe");
        if (IsSpeechRecognitionWorkerInstallation(rootCandidate))
        {
            return rootCandidate;
        }

        var modelDirectory = FindSpeechRecognitionModelDirectory();
        if (modelDirectory is not null)
        {
            var modelCandidate = Path.Combine(
                modelDirectory,
                "VoskWorker",
                "SystemTools.VoskWorker.exe");
            if (IsSpeechRecognitionWorkerInstallation(modelCandidate))
            {
                return modelCandidate;
            }
        }

        var pluginCandidate = Path.Combine(
            GlobalConstants.Information.PluginFolder,
            "VoskWorker",
            "SystemTools.VoskWorker.exe");
        return IsSpeechRecognitionWorkerInstallation(pluginCandidate) ? pluginCandidate : null;
    }

    public static string GetDownloadedSpeechRecognitionWorkerDirectory() =>
        Path.Combine(GetDependencyRoot(), "VoskWorker");

    public static bool HasDownloadedSpeechRecognitionWorker()
    {
        try
        {
            return IsSpeechRecognitionWorkerInstallationDirectory(
                GetDownloadedSpeechRecognitionWorkerDirectory());
        }
        catch
        {
            return false;
        }
    }

    public static bool IsSpeechRecognitionWorkerInstallationDirectory(string directory) =>
        !string.IsNullOrWhiteSpace(directory) &&
        IsSpeechRecognitionWorkerInstallation(
            Path.Combine(directory, "SystemTools.VoskWorker.exe"));

    public static (bool IsAvailable, string Message) CheckSpeechRecognitionDependencies()
    {
        try
        {
            var modelDirectory = FindSpeechRecognitionModelDirectory();
            if (modelDirectory is null)
            {
                return (false, $"找不到经过认证的语音识别模型。请将模型放入 {GetDependencyRoot()} 下。");
            }

            var model = GetSpeechRecognitionModelInfo(modelDirectory);
            if (model is null || model.Kind == SpeechRecognitionModelKind.Unknown)
            {
                return (false, "无法识别当前语音模型类型，请检查 copyright.txt 中的模型名称和模型文件。");
            }

            if (model.Kind == SpeechRecognitionModelKind.SenseVoice)
            {
                var requiredFiles = new[] { "model_quant.onnx", "am.mvn", "tokens.json", "config.yaml" };
                var missingFile = requiredFiles.FirstOrDefault(fileName =>
                    !File.Exists(Path.Combine(modelDirectory, fileName)));
                if (missingFile is not null)
                {
                    return (false, $"SenseVoice 模型文件不完整，缺少 {missingFile}。");
                }
            }

            if (FindSpeechRecognitionWorkerPath() is null)
            {
                return (false, $"找不到语音识别工作进程。请确认 VoskWorker 文件夹位于 {GetDependencyRoot()} 或插件目录下。");
            }

            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, $"检查语音识别依赖失败：{ex.Message}");
        }
    }

    public static SpeechRecognitionModelInfo? GetSpeechRecognitionModelInfo(string modelDirectory)
    {
        if (string.IsNullOrWhiteSpace(modelDirectory))
        {
            return null;
        }

        var markerPath = Path.Combine(modelDirectory, SpeechModelMarkerFileName);
        if (!TryReadSpeechModelMarker(markerPath, out var markerContent))
        {
            return null;
        }

        var firstLineEnd = markerContent.IndexOfAny(['\r', '\n']);
        var firstLine = firstLineEnd >= 0 ? markerContent[..firstLineEnd] : markerContent;
        var modelName = firstLine[SpeechModelMarkerPrefix.Length..]
            .Trim()
            .TrimStart('-', ':', '：')
            .Trim();
        var kind = GetSpeechRecognitionModelKind(modelDirectory, modelName);
        return new SpeechRecognitionModelInfo(
            modelDirectory,
            string.IsNullOrWhiteSpace(modelName) ? null : modelName,
            kind);
    }

    public static string? GetSpeechRecognitionModelName(string modelDirectory) =>
        GetSpeechRecognitionModelInfo(modelDirectory)?.Name;

    public static string? FindVoskModelDirectory() => FindSpeechRecognitionModelDirectory();

    public static string? FindVoskWorkerPath() => FindSpeechRecognitionWorkerPath();

    public static (bool IsAvailable, string Message) CheckVoskDependencies() =>
        CheckSpeechRecognitionDependencies();

    public static string? GetVoskModelName(string modelDirectory) =>
        GetSpeechRecognitionModelName(modelDirectory);

    public static string GetSpeechRecognitionModelDirectory(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName) ||
            modelName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            modelName is "." or "..")
        {
            throw new ArgumentException("Speech recognition model name is invalid.", nameof(modelName));
        }

        return Path.Combine(GetDependencyRoot(), modelName);
    }

    public static bool IsSpeechRecognitionModelInstalled(string modelName)
    {
        try
        {
            var directory = GetSpeechRecognitionModelDirectory(modelName);
            var info = GetSpeechRecognitionModelInfo(directory);
            if (info is null || info.Kind == SpeechRecognitionModelKind.Unknown)
            {
                return false;
            }

            return info.Kind switch
            {
                SpeechRecognitionModelKind.SenseVoice => LooksLikeSenseVoiceModel(directory) &&
                                                         File.Exists(Path.Combine(directory, "config.yaml")),
                SpeechRecognitionModelKind.Vosk => LooksLikeVoskModel(directory),
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    private static SpeechRecognitionModelKind GetSpeechRecognitionModelKind(
        string modelDirectory,
        string modelName)
    {
        if (modelName.Contains("SenseVoice", StringComparison.OrdinalIgnoreCase))
        {
            return SpeechRecognitionModelKind.SenseVoice;
        }

        if (modelName.Contains("Vosk", StringComparison.OrdinalIgnoreCase))
        {
            return SpeechRecognitionModelKind.Vosk;
        }

        if (LooksLikeSenseVoiceModel(modelDirectory))
        {
            return SpeechRecognitionModelKind.SenseVoice;
        }

        if (LooksLikeVoskModel(modelDirectory))
        {
            return SpeechRecognitionModelKind.Vosk;
        }

        return SpeechRecognitionModelKind.Unknown;
    }

    private static bool LooksLikeSenseVoiceModel(string path) =>
        File.Exists(Path.Combine(path, "model_quant.onnx")) &&
        File.Exists(Path.Combine(path, "am.mvn")) &&
        File.Exists(Path.Combine(path, "tokens.json"));

    private static bool LooksLikeVoskModel(string path) =>
        Directory.Exists(Path.Combine(path, "am")) &&
        Directory.Exists(Path.Combine(path, "conf")) &&
        Directory.Exists(Path.Combine(path, "graph"));

    private static bool IsSpeechRecognitionModelDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return false;
        }

        var markerPath = Path.Combine(path, SpeechModelMarkerFileName);
        if (!File.Exists(markerPath))
        {
            return false;
        }

        return TryReadSpeechModelMarker(markerPath, out _);
    }

    private static bool TryReadSpeechModelMarker(string markerPath, out string markerContent)
    {
        markerContent = string.Empty;
        try
        {
            markerContent = File.ReadAllText(markerPath).Trim();
            return markerContent.StartsWith(SpeechModelMarkerPrefix, StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsSpeechRecognitionWorkerInstallation(string executablePath)
    {
        var directory = Path.GetDirectoryName(executablePath);
        return directory is not null &&
               File.Exists(executablePath) &&
               File.Exists(Path.Combine(directory, "SystemTools.VoskWorker.dll")) &&
               File.Exists(Path.Combine(directory, "Vosk.dll")) &&
               File.Exists(Path.Combine(directory, "libvosk.dll")) &&
               File.Exists(Path.Combine(directory, "Microsoft.ML.OnnxRuntime.dll")) &&
               File.Exists(Path.Combine(directory, "onnxruntime.dll")) &&
               File.Exists(Path.Combine(directory, "KaldiNativeFbankSharp.dll")) &&
               File.Exists(Path.Combine(directory, "kaldi-native-fbank.dll")) &&
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
            var requiredPaths = GetFaceRecognitionRequiredPaths();
            if (!Directory.Exists(requiredPaths[0]) ||
                !File.Exists(requiredPaths[1]) ||
                !File.Exists(requiredPaths[2]) ||
                !Directory.Exists(requiredPaths[3]) ||
                !File.Exists(requiredPaths[4]) ||
                !File.Exists(requiredPaths[5]) ||
                !File.Exists(requiredPaths[6]))
            {
                return false;
            }

            var nativeDirectories = GetFaceRecognitionNativeDirectories(GetDependencyRoot());
            var requiredNativeFiles = new[]
            {
                "OpenCvSharpExtern.dll",
                "DlibDotNetNative.dll",
                "DlibDotNetNativeDnn.dll"
            };

            return requiredNativeFiles.All(fileName => nativeDirectories.Any(directory =>
                File.Exists(Path.Combine(directory, fileName))));
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

    private static string[] GetFaceRecognitionNativeDirectories(string dependencyRoot)
    {
        var runtimeIdentifier = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => "win-x86",
            Architecture.Arm64 => "win-arm64",
            _ => "win-x64"
        };

        return
        [
            dependencyRoot,
            Path.Combine(dependencyRoot, "runtimes", runtimeIdentifier, "native"),
            Path.Combine(dependencyRoot, "runtimes", "win", "native"),
            Path.Combine(dependencyRoot, "runtimes")
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
