using System.Runtime.InteropServices;
using SystemTools.Contracts;

namespace SystemTools.Platforms.MacOS;

public sealed class SystemVolumeService : ISystemVolume
{
    private const string Audio = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";
    [StructLayout(LayoutKind.Sequential)]
    private struct Address(uint selector, uint scope, uint element = 0)
    {
        public uint Selector = selector;
        public uint Scope = scope;
        public uint Element = element;
    }

    public Capability Probe()
    {
        try { GetTarget(); return new(CapabilityState.Available); }
        catch (NativeFeatureException error) { return new(error.State, error.Message); }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        { return new(CapabilityState.MissingDependency, "系统音频接口不可用。"); }
    }

    private static (uint Device, Address Property) GetTarget()
    {
        var address = new Address(0x644f7574, 0x676c6f62); // dOut / glob
        uint size = 4;
        Check(GetDevice(1, ref address, 0, 0, ref size, out var device));
        if (device == 0) throw new NativeFeatureException(CapabilityState.MissingDevice, "没有可用的默认输出设备。");
        foreach (var selector in new uint[] { 0x766d7663, 0x766f6c6d }) // vmvc / volm
        {
            address = new(selector, 0x6f757470);
            if (HasProperty(device, ref address) && IsSettable(device, ref address, out var writable) == 0 && writable != 0)
                return (device, address);
        }
        throw new NativeFeatureException(CapabilityState.Unsupported, "当前输出设备不支持系统主音量调整，请更换输出设备。");
    }

    public Task<SystemVolume> ReadAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var (device, property) = GetTarget();
        return Task.FromResult(Read(device, property));
    }

    public Task<SystemVolume> SetAsync(double percent, CancellationToken token)
    {
        if (!double.IsFinite(percent) || percent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(percent), "音量必须在 0–100 之间。");
        token.ThrowIfCancellationRequested();
        var (device, property) = GetTarget();
        var value = (float)(percent / 100);
        Check(SetValue(device, ref property, 0, 0, 4, ref value));
        return Task.FromResult(Read(device, property));
    }

    private static SystemVolume Read(uint device, Address property)
    {
        uint size = 4;
        Check(GetValue(device, ref property, 0, 0, ref size, out var value));
        return new(device.ToString(System.Globalization.CultureInfo.InvariantCulture), value * 100);
    }

    private static void Check(int result)
    {
        if (result != 0) throw new NativeFeatureException(CapabilityState.MissingDevice, "系统音频设备发生变化或暂时不可用，请重试。");
    }

    [DllImport(Audio, EntryPoint = "AudioObjectGetPropertyData")]
    private static extern int GetDevice(uint device, ref Address address, uint qualifierSize, nint qualifier, ref uint size, out uint value);
    [DllImport(Audio, EntryPoint = "AudioObjectGetPropertyData")]
    private static extern int GetValue(uint device, ref Address address, uint qualifierSize, nint qualifier, ref uint size, out float value);
    [DllImport(Audio, EntryPoint = "AudioObjectSetPropertyData")]
    private static extern int SetValue(uint device, ref Address address, uint qualifierSize, nint qualifier, uint size, ref float value);
    [DllImport(Audio, EntryPoint = "AudioObjectHasProperty")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool HasProperty(uint device, ref Address address);
    [DllImport(Audio, EntryPoint = "AudioObjectIsPropertySettable")]
    private static extern int IsSettable(uint device, ref Address address, out byte value);
}
