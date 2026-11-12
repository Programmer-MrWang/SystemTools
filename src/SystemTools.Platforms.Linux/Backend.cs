using SystemTools.Contracts;
namespace SystemTools.Platforms.Linux;
public sealed class Backend : IPlatformBackend
{
    public PlatformDescriptor Describe() => new("linux-x64", "Linux x64 / X11", "M1 加载骨架；仅原生 X11 会话");
}
