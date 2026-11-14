using SystemTools.Contracts;
namespace SystemTools.Platforms.Linux;
public sealed class Backend : INativeDesktopBackend
{
    public NativeDesktopServices CreateServices() => new() { Volume = new SystemVolumeService(), Idle = new UserIdleService() };
    public PlatformDescriptor Describe() => new("linux-x64", "Linux x64 / X11", "M4 原生与共享业务；仅原生 X11 会话");
}
