using SystemTools.Common.Media;
using SystemTools.Contracts;

namespace SystemTools.Platforms.Linux;

public sealed class SpeechRecognitionService : ISpeechRecognition
{
    private readonly VoskStreamingSpeech _speech;
    public SpeechRecognitionService(Func<MediaPreferences> preferences)
    {
        var root = Path.GetDirectoryName(typeof(Backend).Assembly.Location)!;
        var library = Path.Combine(root, "platforms/linux-x64/native/libvosk.so");
        if (!File.Exists(library)) library = Path.Combine(root, "libvosk.so");
        _speech = new(preferences, library, () => MediaFiles.FindExecutable("ffmpeg", preferences().FfmpegPath) is null
            ? new(CapabilityState.MissingDependency, "请安装支持 PulseAudio 的 FFmpeg，或在媒体设置中选择其路径。")
            : new(CapabilityState.Available), token => FfmpegMicrophone.CaptureAsync(
                MediaFiles.FindExecutable("ffmpeg", preferences().FfmpegPath)!, preferences().MicrophoneDevice, token));
    }
    public Capability Probe() => _speech.Probe();
    public IAsyncEnumerable<SpeechSegment> RecognizeAsync(CancellationToken token) => _speech.RecognizeAsync(token);
}
