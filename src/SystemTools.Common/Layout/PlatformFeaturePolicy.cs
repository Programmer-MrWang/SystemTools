using SystemTools.Contracts;

namespace SystemTools.Common.Layout;

public static class PlatformFeaturePolicy
{
    public static IReadOnlyList<string> WindowsExclusive { get; } = Array.AsReadOnly(new[]
    {
        "SystemTools.EnableDevice", "SystemTools.DisableDevice", "SystemTools.EnableMouse", "SystemTools.DisableMouse",
        "SystemTools.RestartAsAdmin", "SystemTools.ShowToast"
    });
    public static void BindWindowsBoundaries(FeatureCatalog catalog)
    {
        if (catalog.RuntimeId == "win-x64") return;
        foreach (var id in WindowsExclusive)
            catalog.Bind(id, () => new(CapabilityState.Unsupported, "此功能依赖 Windows 设备管理、输入驱动、提权或通知接口，当前平台不提供同名模拟。原配置已保留。"));
    }
}
