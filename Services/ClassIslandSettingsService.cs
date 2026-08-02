using System;
using System.Reflection;
using Avalonia.Threading;
using ClassIsland.Core;
using ClassIsland.Shared;

namespace SystemTools.Services;

public sealed class ClassIslandSettingsService
{
    public bool SetTheme(int theme) => SetProperty("Theme", theme);

    public bool SetMainWindowVisible(bool isVisible) => SetProperty("IsMainWindowVisible", isVisible);

    public bool? GetMainWindowVisible() => GetProperty<bool>("IsMainWindowVisible");

    public IDisposable? HideMainWindow()
    {
        var previous = GetMainWindowVisible();
        if (previous is null)
        {
            return null;
        }

        // Setting the same value is reported as "unchanged" by the host
        // settings object. That is still a successful hide operation from the
        // caller's perspective; the lease must remember the original state.
        if (previous.Value && !SetMainWindowVisible(false))
        {
            return null;
        }

        return new RestoreMainWindowVisibility(this, previous.Value);
    }

    public bool? GetWindowCaptureBlockingEnabled() =>
        GetProperty<bool>("IsWindowCaptureBlockingEnabled");

    public string? GetSelectedSpeechProvider()
    {
        var settings = GetSettings();
        var property = settings?.GetType()
            .GetProperty("SelectedSpeechProvider", BindingFlags.Instance | BindingFlags.Public);
        return property?.CanRead == true ? property.GetValue(settings) as string : null;
    }

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

    private sealed class RestoreMainWindowVisibility(
        ClassIslandSettingsService service,
        bool previousValue) : IDisposable
    {
        private ClassIslandSettingsService? _service = service;

        public void Dispose()
        {
            var owner = System.Threading.Interlocked.Exchange(ref _service, null);
            if (owner is null)
            {
                return;
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                owner.SetMainWindowVisible(previousValue);
            }
            else
            {
                Dispatcher.UIThread.Post(() => owner.SetMainWindowVisible(previousValue));
            }
        }
    }
}
