using System.Runtime.InteropServices;

namespace SystemTools.Contracts;

public sealed record PlatformEnvironment(string OS, Architecture Architecture,
    string? SessionType = null, string? Display = null, string? WaylandDisplay = null)
{
    public static PlatformEnvironment Current() => new(
        OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" :
        OperatingSystem.IsMacOS() ? "MacOS" : "Unsupported",
        RuntimeInformation.ProcessArchitecture,
        Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
        Environment.GetEnvironmentVariable("DISPLAY"),
        Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
}

public sealed record PlatformDescriptor(string RuntimeId, string DisplayName, string Scope);

// M1 only describes a backend. Add real OS services when a feature requires them.
public interface IPlatformBackend
{
    PlatformDescriptor Describe();
}

public static class PlatformSelection
{
    public static string Select(PlatformEnvironment environment) => environment switch
    {
        { OS: "Windows", Architecture: Architecture.X64 } => "win-x64",
        { OS: "MacOS", Architecture: Architecture.Arm64 } => "osx-arm64",
        { OS: "Linux", Architecture: Architecture.X64 } when
            string.Equals(environment.SessionType, "x11", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(environment.Display) &&
            string.IsNullOrWhiteSpace(environment.WaylandDisplay) => "linux-x64",
        _ => throw new PlatformNotSupportedException(
            "M1 支持 Windows x64、Linux x64 原生 X11、macOS Apple Silicon 原生 ARM64。" +
            $"当前 OS={environment.OS}, Architecture={environment.Architecture}, Session={environment.SessionType ?? "unknown"}。")
    };

    public static string ModuleName(string runtimeId) => runtimeId switch
    {
        "win-x64" => "SystemTools.Platforms.Windows",
        "linux-x64" => "SystemTools.Platforms.Linux",
        "osx-arm64" => "SystemTools.Platforms.MacOS",
        _ => throw new PlatformNotSupportedException(runtimeId)
    };
}
