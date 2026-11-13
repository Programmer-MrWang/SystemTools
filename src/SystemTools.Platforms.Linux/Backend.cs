using SystemTools.Contracts;
namespace SystemTools.Platforms.Linux;
public sealed class Backend : IPlatformBackend
{
    public PlatformDescriptor Describe() => new("linux-x64", "Linux x64 / X11", "M3 共享业务入口；仅原生 X11 会话，原生扩展待后续接入");
}
