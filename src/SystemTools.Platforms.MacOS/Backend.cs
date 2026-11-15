using SystemTools.Contracts;
namespace SystemTools.Platforms.MacOS;
public sealed class Backend : INativeDesktopBackend, IMediaPlatformBackend
{
    private readonly CompanionClient _companion = new();
    public PlatformMediaServices CreateMediaServices(System.Func<MediaPreferences> preferences) => new()
    {
        Camera = new CameraService(_companion), Speech = new SpeechRecognitionService(preferences, _companion),
        Authentication = new SystemAuthenticationService(), Media = new SystemTools.Common.Media.UnsupportedMediaSessions(),
        Playback = new SystemTools.Common.Media.CommandTextSpeech("osx-arm64")
    };
    public NativeDesktopServices CreateServices() => new() { Advanced = new() { Background = new BackgroundCaptureService(), TextRecognition = new TextRecognitionService() }, Volume = new SystemVolumeService(), Idle = new UserIdleService(), ScreenCapture = new ScreenCaptureService(), UsbDevices = new UsbDevicesService(), Keyboard = new KeyboardService(), Hotkeys = new HotkeysService(), Windows = new WindowOperationsService(), Wallpaper = new WallpaperService(), Camera = new CameraService(_companion) };
    public PlatformDescriptor Describe() => new("osx-arm64", "macOS / Apple Silicon", "M4 原生与共享业务；仅原生 ARM64");
}
