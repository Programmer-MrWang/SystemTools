using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Shared;
using ClassIsland.Shared.Models.Automation;
using Microsoft.Extensions.DependencyInjection;
using SystemTools.Contracts;

namespace SystemTools;

public sealed class DiagnosticService(IPlatformBackend backend, string configDirectory)
{
    public string Summary => $"平台：{backend.Describe().DisplayName}\n{backend.Describe().Scope}\n诊断目录：{configDirectory}";

    public object Capture(string source)
    {
        var context = AssemblyLoadContext.GetLoadContext(typeof(Plugin).Assembly)!;
        var report = new
        {
            schemaVersion = 1, milestone = "M1-preview", source,
            runtimeId = backend.Describe().RuntimeId,
            os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            runtime = RuntimeInformation.FrameworkDescription,
            context = context.Name,
            entry = typeof(Plugin).Assembly.Location,
            contract = typeof(IPlatformBackend).Assembly.Location,
            backend = backend.GetType().Assembly.Location,
            assemblies = context.Assemblies.Select(a => new { name = a.GetName().Name, path = a.Location }).OrderBy(a => a.name).ToArray()
        };
        Save("M1.Diagnostics.json", report);
        return report;
    }

    public async Task RunHostSmokeAsync()
    {
        try
        {
            var provider = IAppHost.Host!.Services;
            var page = await Dispatcher.UIThread.InvokeAsync(() => provider.GetRequiredKeyedService<SettingsPageBase>("SystemTools.M1.Diagnostics"));
            if (page is not DiagnosticsSettingsPage) throw new InvalidOperationException("设置页注册身份错误。");
            var action = provider.GetRequiredKeyedService<ActionBase>("SystemTools.M1.RecordDiagnostics");
            var set = new ActionSet();
            var item = new ActionItem { Id = "SystemTools.M1.RecordDiagnostics" };
            set.ActionItems.Add(item);
            set.SetStartRunning(true);
            try { await action.InvokeAsync(item, set); }
            finally { set.SetEndRunning(true); }
            if (item.Exception != null) throw new InvalidOperationException(item.Exception);
            var context = AssemblyLoadContext.GetLoadContext(typeof(Plugin).Assembly)!;
            var modules = context.Assemblies.Where(a => a.GetName().Name!.StartsWith("SystemTools.Platforms.")).ToArray();
            if (modules.Length != 1 || modules[0] != backend.GetType().Assembly)
                throw new InvalidOperationException("加载了错误或重复的平台模块。");
            if (context.Assemblies.Any(a => a.GetName().Name is "System.Windows.Forms" or "System.Speech" or "OpenCvSharp" or "DlibDotNet" or "SuperAutoIsland.Interface"))
                throw new InvalidOperationException("M1 意外加载了可选/Windows 依赖。");
            Save("M1.HostSmoke.json", new { passed = true, runtimeId = backend.Describe().RuntimeId, page = page.GetType().FullName, action = action.GetType().FullName, item.IsCompleted, diagnostics = Capture("host-smoke-complete") });
        }
        catch (Exception ex)
        {
            Save("M1.HostSmoke.json", new { passed = false, error = ex.ToString() });
        }
    }

    private void Save(string name, object value)
    {
        Directory.CreateDirectory(configDirectory);
        var path = Path.Combine(configDirectory, name);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
