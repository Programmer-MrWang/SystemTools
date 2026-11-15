using SystemTools.Contracts;
namespace SystemTools.Platforms.Linux;
public sealed class Backend : INativeDesktopBackend, IMediaPlatformBackend
{
    private System.Func<MediaPreferences> _preferences = () => new();
    public PlatformMediaServices CreateMediaServices(System.Func<MediaPreferences> preferences)
    {
        _preferences = preferences;
        return new() { Camera = new SystemTools.Common.Media.FfmpegCamera("linux-x64", () => SystemTools.Common.Media.MediaFiles.FindExecutable("ffmpeg", preferences().FfmpegPath)),
            Speech = new SpeechRecognitionService(preferences), Authentication = new SystemTools.Common.Media.UnsupportedAuthentication(), Media = new MediaSessionsService(), Playback = new SystemTools.Common.Media.CommandTextSpeech("linux-x64") };
    }
    public NativeDesktopServices CreateServices() => new() { Advanced = new() { Background = new SystemTools.Common.Layout.UnsupportedBackgroundCapture(), Displays = new DisplayConfigurationService() }, Volume = new SystemVolumeService(), Idle = new UserIdleService(), ScreenCapture = new ScreenCaptureService(), UsbDevices = new UsbDevicesService(), Keyboard = new KeyboardService(), Hotkeys = new HotkeysService(), Windows = new WindowOperationsService(), Wallpaper = new WallpaperService(), Camera = new SystemTools.Common.Media.FfmpegCamera("linux-x64", () => SystemTools.Common.Media.MediaFiles.FindExecutable("ffmpeg", _preferences().FfmpegPath)) };
    public PlatformDescriptor Describe() => new("linux-x64", "Linux x64 / X11", "M4 原生与共享业务；仅原生 X11 会话");
}
