using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.Logging;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Win32;

namespace SystemTools.Actions;

[ActionInfo("SystemTools.SetVolume", "设置系统音量", "\uF013", false)]
public class SetVolumeAction(ILogger<SetVolumeAction> logger) : ActionBase<SetVolumeSettings>
{
    private readonly ILogger<SetVolumeAction> _logger = logger;


    protected override async Task OnInvoke()
    {
        try
        {
            var deviceEnumerator = new MMDeviceEnumeratorWrapper();
            var device = deviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia);

            float volume = Settings.VolumePercent / 100f;
            device.SetMasterVolumeLevelScalar(volume, Guid.Empty);

            _logger.LogInformation($"音量设置为 {Settings.VolumePercent}%");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "设置音量失败");
            throw;
        }
    }
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorCom
{
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(EDataFlow dataFlow, DeviceState stateMask, out IntPtr devices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(ref Guid iid, CLSCTX clsCtx, IntPtr activationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
}

[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    [PreserveSig]
    int RegisterControlChangeNotify(IntPtr notify);

    [PreserveSig]
    int UnregisterControlChangeNotify(IntPtr notify);

    [PreserveSig]
    int GetChannelCount(out uint count);

    [PreserveSig]
    int SetMasterVolumeLevel(float levelDB, Guid eventContext);

    [PreserveSig]
    int SetMasterVolumeLevelScalar(float level, Guid eventContext);

    [PreserveSig]
    int GetMasterVolumeLevel(out float levelDB);

    [PreserveSig]
    int GetMasterVolumeLevelScalar(out float level);

    [PreserveSig]
    int SetChannelVolumeLevel(uint channel, float levelDB, Guid eventContext);

    [PreserveSig]
    int GetChannelVolumeLevel(uint channel, out float levelDB);

    [PreserveSig]
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, Guid eventContext);

    [PreserveSig]
    int GetMute(out bool mute);

    [PreserveSig]
    int GetVolumeStepInfo(out uint step, out uint stepCount);

    [PreserveSig]
    int VolumeStepUp(Guid eventContext);

    [PreserveSig]
    int VolumeStepDown(Guid eventContext);

    [PreserveSig]
    int QueryHardwareSupport(out uint hardwareSupportMask);

    [PreserveSig]
    int GetVolumeRange(out float volumeMinDB, out float volumeMaxDB, out float volumeStepDB);
}

internal enum EDataFlow
{
    eRender,
    eCapture,
    eAll
}

internal enum ERole
{
    eConsole,
    eMultimedia,
    eCommunications
}

[Flags]
internal enum DeviceState
{
    ACTIVE = 0x00000001,
    DISABLED = 0x00000002,
    NOT_PRESENT = 0x00000004,
    UNPLUGGED = 0x00000008,
    MASK_ALL = 0x0000000F
}

[Flags]
internal enum CLSCTX
{
    INPROC_SERVER = 0x1,
    INPROC_HANDLER = 0x2,
    LOCAL_SERVER = 0x4,
    REMOTE_SERVER = 0x10,
    NO_CODE_DOWNLOAD = 0x400,
    NO_CUSTOM_MARSHAL = 0x1000,
    ENABLE_CODE_DOWNLOAD = 0x2000,
    NO_FAILURE_LOG = 0x4000,
    DISABLE_AAA = 0x8000,
    ENABLE_AAA = 0x10000,
    FROM_DEFAULT_CONTEXT = 0x20000,
    ACTIVATE_64_BIT_SERVER = 0x80000,
    ENABLE_CLOAKING = 0x100000,
    APPCONTAINER = 0x400000,
    ACTIVATE_AAA_AS_IU = 0x800000,
    PS_DLL = unchecked((int)0x80000000)
}

internal class MMDeviceEnumeratorWrapper
{
    private readonly IMMDeviceEnumerator _enumerator;

    public MMDeviceEnumeratorWrapper()
    {
        var type = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"));
        _enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(type);
    }

    public IMMDevice GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role)
    {
        _enumerator.GetDefaultAudioEndpoint(dataFlow, role, out var device);
        return device;
    }
}

internal static class MMDeviceExtensions
{
    private static Guid IID_IAudioEndpointVolume = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");

    public static IAudioEndpointVolume GetAudioEndpointVolume(this IMMDevice device)
    {
        var iid = IID_IAudioEndpointVolume;
        device.Activate(ref iid, CLSCTX.INPROC_SERVER, IntPtr.Zero, out var result);
        return (IAudioEndpointVolume)result;
    }

    public static void SetMasterVolumeLevelScalar(this IMMDevice device, float level, Guid eventContext)
    {
        var volume = device.GetAudioEndpointVolume();
        volume.SetMasterVolumeLevelScalar(level, eventContext);
    }

    public static void SetMute(this IMMDevice device, bool mute, Guid eventContext)
    {
        var volume = device.GetAudioEndpointVolume();
        volume.SetMute(mute, eventContext);
    }
}