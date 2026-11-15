using System.Runtime.CompilerServices;
using SystemTools.Common.Media;
using SystemTools.Contracts;

namespace SystemTools.Platforms.MacOS;

public sealed class SpeechRecognitionService : ISpeechRecognition, IAsyncPermissionRequest
{
    private readonly VoskStreamingSpeech _speech;
    public SpeechRecognitionService(Func<MediaPreferences> preferences)
    {
        var root = Path.GetDirectoryName(typeof(Backend).Assembly.Location)!;
        var library = Path.Combine(root, "platforms/osx-arm64/native/libvosk.dylib");
        if (!File.Exists(library)) library = Path.Combine(root, "libvosk.dylib");
        _speech = new(preferences, library, () =>
            preferences().MicrophoneDevice is "" or "default" ? NativeBridge.Probe("microphone.probe") :
                new(CapabilityState.Unsupported, "Mac 语音使用系统默认麦克风，请在系统声音设置中选择设备。"), Capture);
    }
    public Capability Probe() => _speech.Probe();
    public IAsyncEnumerable<SpeechSegment> RecognizeAsync(CancellationToken token) => _speech.RecognizeAsync(token);
    public async Task<Capability> RequestAccessAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = await Task.Run(() => NativeBridge.Call(new { operation = "microphone.request" }), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return NativeBridge.Status(result);
    }
    private static async IAsyncEnumerable<byte[]> Capture([EnumeratorCancellation] CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var id = Guid.NewGuid().ToString("N");
        NativeBridge.Require(new { operation = "microphone.prepare", id });
        try
        {
            using var stop = token.Register(() => NativeBridge.Call(new { operation = "microphone.stop", id }));
            await Task.Run(() => NativeBridge.Require(new { operation = "microphone.start", id })).ConfigureAwait(false);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var result = NativeBridge.Require(new { operation = "microphone.poll", id });
                var bytes = Convert.FromBase64String(result.GetProperty("pcm").GetString()!);
                if (bytes.Length > 0) yield return bytes;
                await Task.Delay(50, token).ConfigureAwait(false);
            }
        }
        finally { NativeBridge.Call(new { operation = "microphone.release", id }); }
    }
}
