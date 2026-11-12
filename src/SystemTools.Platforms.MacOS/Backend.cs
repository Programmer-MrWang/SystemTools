using SystemTools.Contracts;
namespace SystemTools.Platforms.MacOS;
public sealed class Backend : IPlatformBackend
{
    public PlatformDescriptor Describe() => new("osx-arm64", "macOS / Apple Silicon", "M1 加载骨架；仅原生 ARM64，无需 Rosetta");
}
