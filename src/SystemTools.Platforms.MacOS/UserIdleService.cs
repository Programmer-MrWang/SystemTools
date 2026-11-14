using System.Runtime.InteropServices;
using SystemTools.Contracts;

namespace SystemTools.Platforms.MacOS;

public sealed class UserIdleService : IUserIdle
{
    public Capability Probe() => OperatingSystem.IsMacOS() ? new(CapabilityState.Available) : new(CapabilityState.Unsupported);
    public TimeSpan Read()
    {
        var seconds = SecondsSinceLastEvent(0, uint.MaxValue);
        if (!double.IsFinite(seconds) || seconds < 0) throw new IOException("系统暂时无法取得空闲时长。");
        return TimeSpan.FromSeconds(Math.Min(seconds, TimeSpan.MaxValue.TotalSeconds / 2));
    }

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics", EntryPoint = "CGEventSourceSecondsSinceLastEventType")]
    private static extern double SecondsSinceLastEvent(int source, uint type);
}
