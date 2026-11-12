using SystemTools.Contracts;
namespace SystemTools.Platforms.Windows;
public sealed class Backend : IPlatformBackend
{
    public PlatformDescriptor Describe() => new("win-x64", "Windows x64", "M1 加载骨架；完整原版功能将在 M2 接入");
}
