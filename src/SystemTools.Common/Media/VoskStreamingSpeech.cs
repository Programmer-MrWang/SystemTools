using System.Runtime.CompilerServices;
using SystemTools.Contracts;

namespace SystemTools.Common.Media;

public sealed class VoskStreamingSpeech(Func<MediaPreferences> preferences, string library,
    Func<Capability> microphoneProbe, Func<CancellationToken, IAsyncEnumerable<byte[]>> capture) : ISpeechRecognition
{
    private static readonly DeviceReservation Microphone = new();
    public Capability Probe()
    {
        if (!File.Exists(library)) return new(CapabilityState.MissingDependency, "语音原生依赖缺失，请重新安装完整插件包。");
        if (!MediaFiles.IsVoskModel(preferences().SpeechModelPath))
            return new(CapabilityState.MissingDependency, "请在媒体设置中选择完整的 Vosk 离线模型。");
        return microphoneProbe();
    }

    public async IAsyncEnumerable<SpeechSegment> RecognizeAsync([EnumeratorCancellation] CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var capability = Probe();
        if (!capability.CanExecute) throw new NativeFeatureException(capability.State, capability.Reason);
        using var ownership = Microphone.Acquire("microphone", token);
        using var decoder = await Task.Run(() => new VoskDecoder(library, preferences().SpeechModelPath), token).ConfigureAwait(false);
        string? lastPartial = null;
        await foreach (var samples in capture(token).WithCancellation(token).ConfigureAwait(false))
        {
            token.ThrowIfCancellationRequested();
            var result = decoder.AcceptPcm16(samples);
            if (result is null || (!result.IsFinal && result.Text == lastPartial)) continue;
            lastPartial = result.IsFinal ? null : result.Text;
            yield return result;
        }
        token.ThrowIfCancellationRequested();
        if (decoder.Finish() is { } final) yield return final;
    }
}
