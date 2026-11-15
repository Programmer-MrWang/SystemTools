using ClassIsland.Core.Abstractions.Services;
using SystemTools.Common.Migration;

namespace SystemTools;

internal static class ReleaseStartup
{
    private static readonly string[] LegacyIds = ["SystemTools-Cross-platform", "SystemTools.CrossPlatform"];
    internal static void Prepare(string configFolder, string version)
    {
        var conflict = IPluginService.LoadedPlugins.FirstOrDefault(x => x.IsEnabled && LegacyIds.Contains(x.Manifest.Id, StringComparer.OrdinalIgnoreCase));
        if (conflict is not null)
            throw new InvalidOperationException($"请先退出宿主并停用旧插件 {conflict.Manifest.Id}，预览并完成数据迁移后再启用 SystemTools。已阻止重复注册，旧配置保留。");
        UpgradeSnapshot.Ensure(configFolder, version);
    }
}
