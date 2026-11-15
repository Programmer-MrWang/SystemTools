using SystemTools.Contracts;
namespace SystemTools.Platforms.Linux;
public sealed class Backend : INativeDesktopBackend
{
    public NativeDesktopServices CreateServices() => new() { Volume = new SystemVolumeService(), Idle = new UserIdleService(), ScreenCapture = new ScreenCaptureService(), UsbDevices = new UsbDevicesService(), Keyboard = new KeyboardService(), Hotkeys = new HotkeysService(), Windows = new WindowOperationsService(), Wallpaper = new WallpaperService(), Camera = new SystemTools.Common.Media.FfmpegCamera("linux-x64", () => SystemTools.Common.Media.MediaFiles.FindExecutable("ffmpeg")) };
    public PlatformDescriptor Describe() => new("linux-x64", "Linux x64 / X11", "M4 原生与共享业务；仅原生 X11 会话");
}
