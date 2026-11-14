using System.Runtime.InteropServices;
using SystemTools.Contracts;

namespace SystemTools.Platforms.Linux;

internal sealed class X11Connection : IDisposable
{
    internal const string Library = "libX11.so.6";
    private static readonly bool Initialized = InitThreads() != 0;
    internal nint Display { get; }
    internal X11Connection()
    {
        if (!OperatingSystem.IsLinux() || !Initialized) throw new NativeFeatureException(CapabilityState.Unsupported, "需要原生 Linux X11 会话。");
        PlatformSelection.Select(PlatformEnvironment.Current());
        Display = OpenDisplay(0);
        if (Display == 0) throw new NativeFeatureException(CapabilityState.MissingDependency, "无法连接 X11 显示服务。");
    }
    public void Dispose() => CloseDisplay(Display);
    [DllImport(Library, EntryPoint = "XInitThreads")] private static extern int InitThreads();
    [DllImport(Library, EntryPoint = "XOpenDisplay")] private static extern nint OpenDisplay(nint name);
    [DllImport(Library, EntryPoint = "XCloseDisplay")] private static extern int CloseDisplay(nint display);
    [DllImport(Library, EntryPoint = "XDefaultRootWindow")] internal static extern nuint Root(nint display);
    [DllImport(Library, EntryPoint = "XFree")] internal static extern int Free(nint memory);
}
