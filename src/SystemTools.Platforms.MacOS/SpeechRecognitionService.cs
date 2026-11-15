using System.Runtime.CompilerServices;
using SystemTools.Common.Media;
using SystemTools.Contracts;

namespace SystemTools.Platforms.MacOS;

public sealed class SpeechRecognitionService : ISpeechRecognition, IAsyncPermissionRequest, IDisposable
{
    private readonly VoskStreamingSpeech _speech;
    private readonly CompanionClient _client;
    private readonly IDisposable _ownership;
    public SpeechRecognitionService(Func<MediaPreferences> preferences) : this(preferences, new CompanionClient()) { }
    internal SpeechRecognitionService(Func<MediaPreferences> preferences, CompanionClient client)
    {
        _client = client;
        _ownership = client.Retain();
        var root = Path.GetDirectoryName(typeof(Backend).Assembly.Location)!;
        var library = Path.Combine(root, "platforms/osx-arm64/native/libvosk.dylib");
        if (!File.Exists(library)) library = Path.Combine(root, "libvosk.dylib");
        _speech = new(preferences, library, () =>
            preferences().MicrophoneDevice is "" or "default" ? _client.Probe("microphone") :
                new(CapabilityState.Unsupported, "Mac 语音使用系统默认麦克风，请在系统声音设置中选择设备。"), Capture);
    }
    public Capability Probe() => _speech.Probe();
    public async IAsyncEnumerable<SpeechSegment> RecognizeAsync([EnumeratorCancellation] CancellationToken token)
    {
        await _client.ProbeAsync("microphone", token).ConfigureAwait(false);
        await foreach (var segment in _speech.RecognizeAsync(token).ConfigureAwait(false)) yield return segment;
    }
    public Task<Capability> RequestAccessAsync(CancellationToken token) => _client.RequestAccessAsync("microphone", token);
    private async IAsyncEnumerable<byte[]> Capture([EnumeratorCancellation] CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var id = Guid.NewGuid().ToString("D");
        try
        {
            await _client.RequireAsync("microphone.prepare", new { id }, token).ConfigureAwait(false);
            using var stop = token.Register(() => _ = _client.EndSessionAsync("microphone.stop", id));
            await _client.RequireAsync("microphone.start", new { id }, token).ConfigureAwait(false);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var result = await _client.RequireAsync("microphone.poll", new { id }, token).ConfigureAwait(false);
                var bytes = Convert.FromBase64String(result.GetProperty("pcm").GetString()!);
                if (bytes.Length > 256 * 1024 || bytes.Length % 2 != 0) throw new IOException("采集助手返回了无效音频数据。");
                if (bytes.Length > 0) yield return bytes;
                await Task.Delay(50, token).ConfigureAwait(false);
            }
        }
        finally { await _client.EndSessionAsync("microphone.release", id).ConfigureAwait(false); }
    }
    public void Dispose() => _ownership.Dispose();
}
