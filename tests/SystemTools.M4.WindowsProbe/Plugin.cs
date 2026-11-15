using System.Text.Json;
using Avalonia.Threading;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SystemTools.M4.WindowsProbe;

[PluginEntrance]
public sealed class Plugin : PluginBase
{
    private string _root = "";

    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        _root = Environment.GetEnvironmentVariable("SystemTools_M4_RejectionRoot")
            ?? throw new InvalidOperationException("Only run this probe in an explicit isolated sandbox.");
        AppBase.Current.AppStarted += OnStarted;
        AppBase.Current.AppStopping += OnStopping;
    }

    private async void OnStarted(object? sender, EventArgs args)
    {
        AppBase.Current.AppStarted -= OnStarted;
        try
        {
            var plugin = IPluginService.LoadedPlugins.Single(item => item.Manifest.Id == "SystemTools");
            var error = plugin.Exception?.ToString() ?? "";
            Save("M4.Rejection.json", new
            {
                passed = plugin.LoadStatus == PluginLoadStatus.Error &&
                    error.Contains("SystemTools.Platforms.Windows", StringComparison.Ordinal) &&
                    error.Contains("FileNotFoundException", StringComparison.Ordinal),
                loadStatus = plugin.LoadStatus.ToString(),
                error
            });
        }
        catch (Exception error)
        {
            Save("M4.Rejection.json", new { passed = false, error = error.ToString() });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => AppBase.Current.Stop());
        }
    }

    private void OnStopping(object? sender, EventArgs args)
    {
        AppBase.Current.AppStopping -= OnStopping;
        Save("M4.RejectionShutdown.json", new { passed = true, appStoppingObserved = true });
    }

    private void Save(string name, object value) => File.WriteAllText(Path.Combine(_root, name),
        JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
}
