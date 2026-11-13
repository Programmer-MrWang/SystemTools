using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Services.Registry;
using ClassIsland.Shared;
using Microsoft.Extensions.DependencyInjection;
using SystemTools.Shared;

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
            var result = new
            {
                passed = constructed.Length >= 5 && themes.Length == 3 && assets,
                stage, pages = constructed, themes, legacyImageUris = assets,
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
}

