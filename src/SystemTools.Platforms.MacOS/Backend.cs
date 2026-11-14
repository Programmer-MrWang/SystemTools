using SystemTools.Contracts;
namespace SystemTools.Platforms.MacOS;
public sealed class Backend : INativeDesktopBackend
{
    public NativeDesktopServices CreateServices() => new() { Volume = new SystemVolumeService() };
    public PlatformDescriptor Describe() => new("osx-arm64", "macOS / Apple Silicon", "M4 原生与共享业务；仅原生 ARM64");
}
