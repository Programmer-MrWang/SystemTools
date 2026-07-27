using System.Reflection;
using ClassIsland.Core;
using ClassIsland.Shared;

namespace SystemTools.Services;

public sealed class ClassIslandSettingsService
{
    public bool SetTheme(int theme) => SetProperty("Theme", theme);

    public bool SetMainWindowVisible(bool isVisible) => SetProperty("IsMainWindowVisible", isVisible);

    public bool? GetWindowCaptureBlockingEnabled() =>
        GetProperty<bool>("IsWindowCaptureBlockingEnabled");

    private static T? GetProperty<T>(string propertyName) where T : struct
    {
        var settings = GetSettings();
        var property = settings?.GetType()
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        return property?.CanRead == true && property.GetValue(settings) is T value ? value : null;
    }

    private static bool SetProperty<T>(string propertyName, T value)
    {
        var settings = GetSettings();
        var property = settings?.GetType()
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property?.CanRead != true || property.CanWrite != true)
        {
            return false;
        }

        if (Equals(property.GetValue(settings), value))
        {
            return false;
        }

        property.SetValue(settings, value);
        return true;
    }

    private static object? GetSettings()
    {
        var mainWindow = AppBase.Current.MainWindow;
        var settingsServiceType = mainWindow?.GetType().Assembly
            .GetType("ClassIsland.Services.SettingsService");
        if (settingsServiceType == null)
        {
            return null;
        }

        var settingsService = IAppHost.Host?.Services.GetService(settingsServiceType);
        return settingsServiceType
            .GetProperty("Settings", BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(settingsService);
    }
}
