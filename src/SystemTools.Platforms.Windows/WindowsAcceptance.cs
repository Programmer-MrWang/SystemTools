using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Runtime.Loader;
using Avalonia.Markup.Xaml;
using Avalonia.Controls;
using Avalonia.Interactivity;
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
    private static object? _directoryValidation;
    public static async Task RunAsync(int stage)
    {
        var destination = Path.Combine(GlobalConstants.PluginConfigFolder!, "M2.Acceptance.json");
        try
        {
            var provider = IAppHost.Host!.Services;
            var pages = SettingsWindowRegistryService.Registered
                .Where(page => page.Id.StartsWith("systemtools.", StringComparison.OrdinalIgnoreCase)).ToArray();
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
            var componentsCount = ComponentRegistryService.Registered.Count(info => info.ComponentType?.Assembly == typeof(Backend).Assembly || info.ComponentType?.Assembly == typeof(global::SystemTools.SharedHostRegistration).Assembly);
            var registrations = componentsCount == 7 && IAutomationService.RegisteredTriggers.Count(info => info.Id.StartsWith("SystemTools.")) == 7 && IRulesetService.Rules.Keys.Count(id => id.StartsWith("SystemTools.")) == 5;
            var dpapi = new WindowsDpapiProtector();
            var encrypted = dpapi.Protect("M3-fixture-secret");
            var credentials = dpapi.TryUnprotect(encrypted, out var decrypted) && decrypted == "M3-fixture-secret" && !dpapi.TryUnprotect("dpapi-current-user:v1:REDACTED", out _);
            var sharedDirectory = typeof(global::SystemTools.SharedHostRegistration).Assembly.Location;
            var migrationInteraction = await CheckMigrationInteractionAsync(provider);
            var floatingSnapshots = await CheckFloatingSnapshotsAsync(provider);

            var context = AssemblyLoadContext.GetLoadContext(typeof(Backend).Assembly)!;
            var isolated = context.Assemblies.Count(assembly =>
                    assembly.GetName().Name!.StartsWith("SystemTools.Platforms.")) == 1 &&
                !context.Assemblies.Any(assembly =>
                    assembly.GetName().Name!.StartsWith("ClassIsland.") ||
                    assembly.GetName().Name!.StartsWith("Avalonia") ||
                    assembly.GetName().Name is "WinRT.Runtime" or "Microsoft.Windows.SDK.NET");
            var result = new
            {
                passed = constructed.Length == 8 && themes.Length == 3 && registrations && credentials && migrationInteraction && floatingSnapshots && assets && fileActions && screenshot && excel && unavailableCamera && nativeLibraries && isolated,
                stage, pages = constructed, themes, legacyImageUris = assets,
                fileActions, screenshot, actionFactories, excel, unavailableCamera, nativeLibraries, registrations, credentials, migrationInteraction, floatingSnapshots, sharedDirectory,
                directoryValidation = _directoryValidation,
                isolated, loadedAssemblies = context.Assemblies.Select(assembly => assembly.GetName().Name).OrderBy(name => name).ToArray(),
                components = ComponentRegistryService.Registered
                    .Where(info => info.ComponentType?.Assembly == typeof(Backend).Assembly || info.ComponentType?.Assembly == typeof(global::SystemTools.SharedHostRegistration).Assembly)
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
            if (Environment.GetEnvironmentVariable("SystemTools_M3_GracefulStop") == "1")
                await Dispatcher.UIThread.InvokeAsync(() => ClassIsland.Core.AppBase.Current.Stop());
        }
        catch (Exception exception)
        {
            File.WriteAllText(destination, JsonSerializer.Serialize(new { passed = false, stage, error = exception.ToString() },
                new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static async Task<bool> CheckFloatingSnapshotsAsync(IServiceProvider provider)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var action = provider.GetRequiredKeyedService<ActionBase>("SystemTools.SwitchFloatingWindowTheme");
            var config = GlobalConstants.MainConfig!.Data;
            var original = config.FloatingWindowTheme;
            var first = new ActionItem { Id = "SystemTools.SwitchFloatingWindowTheme", Settings = new Settings.SwitchFloatingWindowThemeSettings { TargetTheme = 2 } };
            var second = new ActionItem { Id = first.Id, Settings = new Settings.SwitchFloatingWindowThemeSettings { TargetTheme = 1 } };
            var set = new ActionSet(); set.ActionItems.Add(first); set.ActionItems.Add(second); set.SetStartRunning(true);
            try
            {
                await action.InvokeAsync(first, set, isRevertable: true);
                await action.InvokeAsync(second, set, isRevertable: true);
                await action.RevertAsync(first, set);
                if (config.FloatingWindowTheme != 1) return false;
                await action.RevertAsync(second, set);
                return config.FloatingWindowTheme == original;
            }
            finally { set.SetEndRunning(true); config.FloatingWindowTheme = original; }
        });
    }

    private static async Task<bool> CheckMigrationInteractionAsync(IServiceProvider provider)
    {
        var root = GlobalConstants.PluginConfigFolder!;
        var legacy = Path.Combine(root, "M3-import-fixture");
        Directory.CreateDirectory(legacy);
        var source = Path.Combine(legacy, "M3-imported-fixture.json");
        var target = Path.Combine(root, "M3-imported-fixture.json");
        File.WriteAllText(source, "{\"enabledActions\":{\"SystemTools.CrossPlatform.Copy\":true},\"text\":\"SystemTools.CrossPlatform.Copy\"}");
        File.WriteAllText(target, "{\"keepBeforeMigration\":true}");
        TextBlock? output = null;
        var prepared = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var page = provider.GetRequiredKeyedService<SettingsPageBase>("SystemTools.Migration");
            var panel = (StackPanel)((ScrollViewer)page.Content!).Content!;
            panel.Children.OfType<TextBox>().Single().Text = legacy;
            var buttons = panel.Children.OfType<Button>().ToArray();
            buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var rows = panel.Children.OfType<StackPanel>().Single();
            var row = rows.Children.OfType<StackPanel>().Single();
            var choice = row.Children.OfType<ComboBox>().Single();
            if (choice.SelectedIndex != 0 || !buttons[1].IsEnabled) return false;
            choice.SelectedIndex = 1;
            output = panel.Children.OfType<TextBlock>().Last();
            buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return true;
        });
        if (!prepared) return false;
        for (var i = 0; i < 100; i++)
        {
            if (await Dispatcher.UIThread.InvokeAsync(() => output!.Text?.StartsWith("导入完成", StringComparison.Ordinal) == true))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(target));
                var imported = document.RootElement.GetProperty("enabledActions").GetProperty("SystemTools.Copy").GetBoolean() &&
                    document.RootElement.GetProperty("text").GetString() == "SystemTools.CrossPlatform.Copy";
                var journal = Directory.EnumerateFiles(Path.Combine(root, ".migrations"), "journal.json", SearchOption.AllDirectories).Single();
                SystemTools.Common.Migration.IdentityMigration.Rollback(journal);
                return imported && File.ReadAllText(target) == "{\"keepBeforeMigration\":true}" && File.Exists(source);
            }
            await Task.Delay(50);
        }
        return false;
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
        if (File.Exists(moved) || !File.Exists(source)) return false;
        var sourceDirectory = Path.Combine(root, "directory-source");
        var copyParent = Path.Combine(root, "directory-copy");
        var moveParent = Path.Combine(root, "directory-move");
        Directory.CreateDirectory(Path.Combine(sourceDirectory, "nested"));
        File.WriteAllText(Path.Combine(sourceDirectory, "nested", "content.txt"), "M3 native metadata copy");
        var originalTarget = Path.Combine(copyParent, "directory-source");
        Directory.CreateDirectory(originalTarget);
        File.WriteAllText(Path.Combine(originalTarget, "original-target.txt"), "keep-original-target");
        try
        {
            await Run("SystemTools.Copy", new Settings.CopySettings { SourcePath = sourceDirectory, DestinationPath = copyParent, OperationType = "文件夹" });
        }
        catch (UnauthorizedAccessException)
        {
            var baseline = await SystemTools.Common.Processes.CommandRunner.RunAsync("robocopy.exe",
                [sourceDirectory, Path.Combine(root, "original-baseline-copy"), "/e", "/copyall", "/r:0", "/w:0", "/nfl", "/ndl", "/np"]);
            var sourceAndTargetPreserved = File.Exists(Path.Combine(sourceDirectory, "nested", "content.txt")) && File.ReadAllText(Path.Combine(originalTarget, "original-target.txt")) == "keep-original-target";
            _directoryValidation = new { outcome = "permission-denied-original-baseline-also-fails", baselineExitCode = baseline.ExitCode,
                sourceAndTargetPreserved, originalCopyAllSemanticsRetained = true, privilegedSuccessRuntimeVerified = false };
            return baseline.ExitCode >= 8 && sourceAndTargetPreserved;
        }
        var copyDirectory = Path.Combine(copyParent, "directory-source");
        if (!File.Exists(Path.Combine(copyDirectory, "nested", "content.txt"))) return false;
        await Run("SystemTools.Move", new Settings.MoveSettings { SourcePath = copyDirectory, DestinationPath = moveParent, OperationType = "文件夹" });
        var moveDirectory = Path.Combine(moveParent, "directory-source");
        if (Directory.Exists(copyDirectory) || !File.Exists(Path.Combine(moveDirectory, "nested", "content.txt"))) return false;
        await Run("SystemTools.Delete", new Settings.DeleteSettings { TargetPath = moveDirectory, OperationType = "文件夹" });
        _directoryValidation = new { outcome = "native-directory-copy-move-delete-passed", privilegedSuccessRuntimeVerified = true };
        return !Directory.Exists(moveDirectory) && Directory.Exists(sourceDirectory);
    }
}

