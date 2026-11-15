using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using ClassIsland.Shared;
using SystemTools.Contracts;
using SystemTools.Services;
using SystemTools.Shared;

namespace SystemTools.Platforms.Windows;

public sealed class WindowsSpeechRecognition(Func<MediaPreferences> preferences) : ISpeechRecognition
{
    public Capability Probe()
    {
        if (preferences().MicrophoneDevice is not ("" or "default"))
            return new(CapabilityState.Unsupported, "Windows 语音使用系统默认麦克风，请在系统声音设置中选择设备。");
        var result = DependencyPaths.CheckSpeechRecognitionDependencies();
        return result.IsAvailable ? new(CapabilityState.Available) : new(CapabilityState.MissingDependency, result.Message);
    }

    public async IAsyncEnumerable<SpeechSegment> RecognizeAsync([EnumeratorCancellation] CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var capability = Probe();
        if (!capability.CanExecute) throw new NativeFeatureException(capability.State, capability.Reason);
        var speech = IAppHost.GetService<VoskSpeechService>();
        var keywords = IAppHost.GetService<KeywordSpeechService>();
        var channel = Channel.CreateBounded<SpeechSegment>(128);
        void Error(string message) => channel.Writer.TryComplete(new IOException(message));
        using var suspension = keywords.SuspendListening();
        using var model = await speech.TryAcquireModelAsync(Error, token).ConfigureAwait(false)
            ?? throw new IOException("语音识别服务正忙或模型尚未就绪。");
        using var capture = await speech.TryStartCaptureAsync((text, final) =>
        {
            if (!channel.Writer.TryWrite(new(text, final))) Error("语音结果处理过慢，采集已停止，请重试。");
        }, Error, token).ConfigureAwait(false) ?? throw new IOException("麦克风不可用或正被其他语音会话占用。");
        await foreach (var segment in channel.Reader.ReadAllAsync(token).ConfigureAwait(false))
        {
            token.ThrowIfCancellationRequested();
            yield return segment;
        }
    }
}
