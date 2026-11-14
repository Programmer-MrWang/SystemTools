using System.Runtime.InteropServices;
using SystemTools.Contracts;

namespace SystemTools.Platforms.Linux;

public sealed class UserIdleService : IUserIdle
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Info
    {
        public nuint Window;
        public int State, Kind;
        public nuint TilOrSince, Idle, EventMask;
    }
    public Capability Probe()
    {
        try { Read(); return new(CapabilityState.Available); }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or NativeFeatureException)
        { return new(CapabilityState.MissingDependency, "请检查原生 X11 会话与 XScreenSaver 扩展。"); }
    }
    public TimeSpan Read()
    {
        using var connection = new X11Connection();
        if (QueryExtension(connection.Display, out _, out _) == 0)
            throw new NativeFeatureException(CapabilityState.MissingDependency, "X11 没有启用空闲查询扩展。");
        var memory = AllocInfo();
        if (memory == 0) throw new OutOfMemoryException();
        try
        {
            if (QueryInfo(connection.Display, X11Connection.Root(connection.Display), memory) == 0)
                throw new NativeFeatureException(CapabilityState.MissingDevice, "无法读取 X11 空闲时长。");
            return TimeSpan.FromMilliseconds(Marshal.PtrToStructure<Info>(memory).Idle);
        }
        finally { X11Connection.Free(memory); }
    }
    [DllImport("libXss.so.1", EntryPoint = "XScreenSaverAllocInfo")] private static extern nint AllocInfo();
    [DllImport("libXss.so.1", EntryPoint = "XScreenSaverQueryExtension")] private static extern int QueryExtension(nint display, out int eventBase, out int errorBase);
    [DllImport("libXss.so.1", EntryPoint = "XScreenSaverQueryInfo")] private static extern int QueryInfo(nint display, nuint window, nint info);
}
