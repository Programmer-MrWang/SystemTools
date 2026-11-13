using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Runtime.Loader;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Shared.Models.Automation;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Services.Registry;
using ClassIsland.Shared;
using Microsoft.Extensions.DependencyInjection;
using SystemTools.Shared;
using SystemTools.Services;
using SystemTools.Models;
using NPOI.XSSF.UserModel;
using NPOI.HSSF.UserModel;

namespace SystemTools.Platforms.Windows;

internal static class WindowsAcceptance
{
    public static async Task RunAsync(int stage)
    {
        var destination = Path.Combine(GlobalConstants.PluginConfigFolder!, "M2.Acceptance.json");
        try
        {
            var provider = IAppHost.Host!.Services;
            var pages = SettingsWindowRegistryService.Registered
                .Where(page => page.Id.StartsWith("systemtools.settings.", StringComparison.Ordinal)).ToArray();
            var constructed = await Dispatcher.UIThread.InvokeAsync(() =>
                pages.Select(page => provider.GetRequiredKeyedService<SettingsPageBase>(page.Id)
                    .GetType().FullName).ToArray());
            var themes = await Dispatcher.UIThread.InvokeAsync(() =>
                new[] { "CardTypeComponent", "ClassWidgets", "NotchStyle" }
                    .Select(name => AvaloniaXamlLoader.Load(
                        new Uri("avares://SystemTools/Themes/" + name + "/Styles.axaml")).GetType().FullName)
                    .ToArray());
            var assets = new[] { "上课.png", "无课程.png", "课间休息.png" }
                .All(name => AssetLoader.Exists(new Uri("avares://SystemTools/Themes/ClassWidgets/" + name)));
            var main = GlobalConstants.MainConfig!;
            var keyBefore = JsonSerializer.Serialize(main.Data).Length;
            main.Save();
            var fileActions = stage < 3 || await CheckFileActionsAsync(provider);
            var actionFactories = stage < 3 ? Array.Empty<string>() :
                IActionService.ActionInfos.Keys.Where(key => key.StartsWith("SystemTools."))
                    .Select(key => provider.GetRequiredKeyedService<ActionBase>(key).GetType().FullName!)
                    .ToArray();
            var screenshot = stage < 4 || await CheckScreenshotAsync(provider);
            var excel = stage < 5 || CheckExcel(provider);
            var unavailableCamera = stage < 5 || await CheckUnavailableCameraAsync(provider);
            var nativeLibraries = stage < 6 || CheckNativeLibraries();
            var context = AssemblyLoadContext.GetLoadContext(typeof(Backend).Assembly)!;
            var isolated = context.Assemblies.Count(assembly =>
                    assembly.GetName().Name!.StartsWith("SystemTools.Platforms.")) == 1 &&
                !context.Assemblies.Any(assembly =>
                    assembly.GetName().Name!.StartsWith("ClassIsland.") ||
                    assembly.GetName().Name!.StartsWith("Avalonia") ||
                    assembly.GetName().Name is "WinRT.Runtime" or "Microsoft.Windows.SDK.NET");
            var result = new
            {
                passed = constructed.Length == 6 && themes.Length == 3 && assets && fileActions && screenshot && excel && unavailableCamera && nativeLibraries && isolated,
                stage, pages = constructed, themes, legacyImageUris = assets,
                fileActions, screenshot, actionFactories, excel, unavailableCamera, nativeLibraries,
                isolated, loadedAssemblies = context.Assemblies.Select(assembly => assembly.GetName().Name).OrderBy(name => name).ToArray(),
                components = ComponentRegistryService.Registered
                    .Where(info => info.ComponentType?.Assembly == typeof(Backend).Assembly)
                    .Select(info => info.Guid).OrderBy(id => id).ToArray(),
                triggers = IAutomationService.RegisteredTriggers.Where(info => info.Id.StartsWith("SystemTools."))
                    .Select(info => info.Id).OrderBy(id => id).ToArray(),
                rules = IRulesetService.Rules.Keys.Where(id => id.StartsWith("SystemTools.")).OrderBy(id => id).ToArray(),
                actions = IActionService.ActionInfos.Keys.Where(key => key.StartsWith("SystemTools.")).OrderBy(key => key).ToArray(),
                configSaved = File.Exists(Path.Combine(GlobalConstants.PluginConfigFolder!, "Main.json")),
                configSerializedLength = keyBefore,
                backendAssembly = typeof(Backend).Assembly.Location
            };
            File.WriteAllText(destination, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception)
        {
            File.WriteAllText(destination, JsonSerializer.Serialize(new { passed = false, stage, error = exception.ToString() },
                new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static bool CheckNativeLibraries()
    {
        if (string.IsNullOrWhiteSpace(OpenCvSharp.Cv2.GetVersionString())) return false;
        using var detector = DlibDotNet.Dlib.GetFrontalFaceDetector();
        var path = DependencyPaths.FindSpeechRecognitionWorkerPath();
        return path != null && File.Exists(path);
    }

    private static async Task<bool> CheckUnavailableCameraAsync(IServiceProvider provider)
    {
        var action = provider.GetRequiredKeyedService<ActionBase>("SystemTools.CameraCapture");
        if (action is not UnavailableCameraAction) return true;
        var item = new ActionItem { Id = "SystemTools.CameraCapture" };
        var set = new ActionSet();
        set.SetStartRunning(true);
        try
        {
            await action.InvokeAsync(item, set);
            return false;
        }
        catch (InvalidOperationException)
        {
            return item.IsCompleted && item.Exception != null;
        }
        finally { set.SetEndRunning(true); }
    }

    private static bool CheckExcel(IServiceProvider provider)
    {
        var exporter = provider.GetRequiredService<ClassIslandProfileExcelExporter>();
        var profile = new ClassIslandProfileDocument { Name = "M2 acceptance" };
        profile.Subjects["sample"] = new ClassIslandSubject { Name = "数学", TeacherName = "测试教师" };
        var source = new ProfileExportSource { FilePath = "M2-synthetic.json", Profile = profile };
        var folder = Path.Combine(GlobalConstants.PluginConfigFolder!, "M2-excel");
        Directory.CreateDirectory(folder);
        foreach (var xlsx in new[] { true, false })
        {
            var data = exporter.BuildWorkbook(source,
                new ProfileExcelExportOptions { ExportSubject = true, UseXlsx = xlsx });
            var file = Path.Combine(folder, xlsx ? "subject.xlsx" : "subject.xls");
            File.WriteAllBytes(file, data);
            using var input = new MemoryStream(data);
            using NPOI.SS.UserModel.IWorkbook workbook = xlsx ? new XSSFWorkbook(input) : new HSSFWorkbook(input);
            if (workbook.NumberOfSheets != 1 || workbook.GetSheetAt(0).LastRowNum < 1) return false;
        }
        return true;
    }

    private static async Task<bool> CheckScreenshotAsync(IServiceProvider provider)
    {
        var folder = Path.Combine(GlobalConstants.PluginConfigFolder!, "M2-screenshot");
        var action = provider.GetRequiredKeyedService<ActionBase>("SystemTools.ScreenShot");
        var item = new ActionItem
        {
            Id = "SystemTools.ScreenShot",
            Settings = new Settings.ScreenShotSettings { SaveFolder = folder }
        };
        var set = new ActionSet();
        set.ActionItems.Add(item);
        set.SetStartRunning(true);
        try { await action.InvokeAsync(item, set); }
        finally { set.SetEndRunning(true); }
        return item.IsCompleted && item.Exception == null &&
               Directory.EnumerateFiles(folder, "*.png").Any(path => new FileInfo(path).Length > 100);
    }

    private static async Task<bool> CheckFileActionsAsync(IServiceProvider provider)
    {
        var root = Path.Combine(GlobalConstants.PluginConfigFolder!, "M2-file-tests");
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "source.txt");
        var copied = Path.Combine(root, "copied.txt");
        var moved = Path.Combine(root, "moved.txt");
        File.WriteAllText(source, "M2 file lifecycle");
        async Task Run(string id, object settings)
        {
            var action = provider.GetRequiredKeyedService<ActionBase>(id);
            var item = new ActionItem { Id = id, Settings = settings };
            var set = new ActionSet();
            set.ActionItems.Add(item);
            set.SetStartRunning(true);
            try { await action.InvokeAsync(item, set); }
            finally { set.SetEndRunning(true); }
            if (!item.IsCompleted || item.Exception != null)
                throw new InvalidOperationException("File action did not complete: " + id);
        }
        await Run("SystemTools.Copy", new Settings.CopySettings { SourcePath = source, DestinationPath = copied });
        if (File.ReadAllText(copied) != File.ReadAllText(source)) return false;
        await Run("SystemTools.Move", new Settings.MoveSettings { SourcePath = copied, DestinationPath = moved });
        if (File.Exists(copied) || !File.Exists(moved)) return false;
        await Run("SystemTools.Delete", new Settings.DeleteSettings { TargetPath = moved });
        return !File.Exists(moved) && File.Exists(source);
    }
}

