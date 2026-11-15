using System.Text.Json;
using System.Text.Json.Serialization;

namespace SystemTools.Contracts;

public sealed class MediaPreferences
{
    public string FfmpegPath { get; set; } = "";
    public string SpeechModelPath { get; set; } = "";
    public string MicrophoneDevice { get; set; } = "default";
    public bool EnableSystemAuthentication { get; set; }
    public string LyricsFile { get; set; } = "";
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public interface IMediaPlatformBackend
{
    PlatformMediaServices CreateMediaServices(Func<MediaPreferences> preferences);
}

public interface IAsyncPermissionRequest
{
    Task<Capability> RequestAccessAsync(CancellationToken token);
}

public interface IAsyncCapabilityProbe
{
    Task<Capability> ProbeAsync(CancellationToken token);
}

public sealed record CaptureDevice(string Id, string Name);
public interface ICameraCapture : INativeCapability
{
    Task<IReadOnlyList<CaptureDevice>> ListDevicesAsync(CancellationToken token);
    Task CapturePngAsync(string deviceId, string destination, CancellationToken token);
}

public sealed record SpeechSegment(string Text, bool IsFinal, double? Confidence = null);
public interface ISpeechRecognition : INativeCapability
{
    IAsyncEnumerable<SpeechSegment> RecognizeAsync(CancellationToken token);
}

public sealed record AuthenticationResult(bool Verified, string Message);
public interface ISystemAuthentication : INativeCapability
{
    Task<AuthenticationResult> VerifyAsync(string reason, nint parentWindow, CancellationToken token);
}

public sealed record MediaSession(string Id, string State, string Title = "", string Artist = "", string? Lyrics = null, TimeSpan? Position = null);
public interface IMediaSessions : INativeCapability
{
    Task<IReadOnlyList<MediaSession>> ReadAsync(CancellationToken token);
}

public interface ITextSpeech : INativeCapability
{
    Task SpeakAsync(string text, CancellationToken token);
}

public sealed class PlatformMediaServices : IDisposable
{
    public required ICameraCapture Camera { get; init; }
    public required ISpeechRecognition Speech { get; init; }
    public required ISystemAuthentication Authentication { get; init; }
    public required IMediaSessions Media { get; init; }
    public ITextSpeech? Playback { get; init; }
    private readonly CancellationTokenSource _stop = new();
    private int _disposed;
    public CancellationToken Stopping => _stop.Token;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        List<Exception> failures = [];
        try { _stop.Cancel(); } catch (Exception error) { failures.Add(error); }
        foreach (var resource in new object?[] { Camera, Speech, Authentication, Media, Playback }
                     .OfType<IDisposable>().Distinct<IDisposable>(ReferenceEqualityComparer.Instance))
            try { resource.Dispose(); } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("部分媒体资源未能完成清理。", failures);
    }
}
