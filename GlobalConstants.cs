using Microsoft.Extensions.Logging;
using SystemTools.ConfigHandlers;
using Microsoft.Extensions.Logging.Console;
using System.Reflection;

namespace SystemTools.Shared;

public static class GlobalConstants
{
    public static string? PluginConfigFolder { get; set; }

    public static ConfigHandlers.MainConfigHandler? MainConfig { get; set; }

    public static class HostInterfaces
    {
        public static ILogger<Plugin>? PluginLogger;
    }

    public static class Information
    {
        public static string PluginFolder { get; set; } = string.Empty;
        public static string PluginVersion { get; set; } = "???";
    }

    public static bool ShowChangelogOnOpen { get; set; } = false;
}


