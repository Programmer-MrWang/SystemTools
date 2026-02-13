using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;

namespace SystemTools.Shared;

public static class InjectServices
{
    public static bool TryGetAddSettingsPageGroupMethod([MaybeNullWhen(false)] out MethodInfo method)
    {
        var settingsWindowRegistryExtensionsType = typeof(SettingsWindowRegistryExtensions);
        method = settingsWindowRegistryExtensionsType
            .GetMethods()
            .FirstOrDefault(m => (m.ToString()?.Contains("AddSettingsPageGroup") ?? false)
                                 && m.GetParameters().Length == 4);
        return method != null;
    }

    public static FieldInfo GetSettingsPageInfoNameField()
    {
        var settingsPageInfoType = typeof(SettingsPageInfo);
        var field = settingsPageInfoType
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(f => f.ToString()?.Contains("Name") ?? false);
        return field!;
    }

    public static PropertyInfo GetSettingsPageInfoGroupIdProperty()
    {
        var settingsPageInfoType = typeof(SettingsPageInfo);
        var property = settingsPageInfoType
            .GetProperties()
            .FirstOrDefault(p => p.ToString()?.Contains("GroupId") ?? false);
        return property!;
    }
}