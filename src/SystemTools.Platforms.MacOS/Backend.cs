using SystemTools.Contracts;
namespace SystemTools.Platforms.MacOS;
public sealed class Backend : IPlatformBackend
{
    public PlatformDescriptor Describe() => new("osx-arm64", "macOS / Apple Silicon", "M3 共享业务入口；仅原生 ARM64，原生扩展待后续接入");
}
