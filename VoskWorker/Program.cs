using System.Diagnostics;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Vosk;

namespace SystemTools.VoskWorker;

internal static class Program
{
    private const int SampleRate = 16000;
    private const int SpeechLevelThreshold = 450;
    private static readonly long SpeechActivityIntervalTicks = Stopwatch.Frequency / 5;
    private static readonly object OutputLock = new();
    private static readonly SemaphoreSlim CaptureLock = new(1, 1);
    private static Model? _model;
    private static CaptureSession? _capture;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length != 1 || !Directory.Exists(args[0]))
            {
                WriteMessage("error", message: "Vosk 模型目录无效。");
                return 2;
            }

            Vosk.Vosk.SetLogLevel(-1);
            _model = await Task.Run(() => new Model(args[0]));
            WriteMessage("model_ready");
            await MonitorParentCommandsAsync();
            await StopCaptureAsync();
            return 0;
        }
        catch (Exception ex)
        {
            WriteMessage("error", message: $"Vosk 工作进程失败：{ex.Message}");
            return 1;
        }
        finally
        {
            if (_capture is not null)
            {
                await StopCaptureAsync();
            }
            _model?.Dispose();
            _model = null;
        }
    }

    private static async Task MonitorParentCommandsAsync()
    {
        while (true)
        {
            var command = await Console.In.ReadLineAsync();
            if (command is null ||
                string.Equals(command, "shutdown", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(command, "stop", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.Equals(command, "start_capture", StringComparison.OrdinalIgnoreCase))
            {
                await StartCaptureAsync();
            }
            else if (string.Equals(command, "stop_capture", StringComparison.OrdinalIgnoreCase))
            {
                await StopCaptureAsync();
            }
        }
    }

    private static async Task StartCaptureAsync()
    {
        await CaptureLock.WaitAsync();
        try
        {
            if (_capture is not null)
            {
                WriteMessage("capture_started");
                return;
            }

            if (_model is null)
            {
                WriteMessage("error", message: "Vosk 模型尚未加载。");
                return;
            }

            var session = new CaptureSession(_model);
            _capture = session;
            session.AudioReceived += OnAudioReceived;
            session.StoppedUnexpectedly += OnCaptureStoppedUnexpectedly;
            session.Start();
        }
        catch (Exception ex)
        {
            _capture?.Dispose();
            _capture = null;
            WriteMessage("error", message: $"无法打开麦克风：{ex.Message}");
        }
        finally
        {
            CaptureLock.Release();
        }
    }

    private static async Task StopCaptureAsync()
    {
        await CaptureLock.WaitAsync();
        try
        {
            var session = _capture;
            if (session is null)
            {
                WriteMessage("capture_stopped");
                return;
            }

            _capture = null;
            session.AudioReceived -= OnAudioReceived;
            session.StoppedUnexpectedly -= OnCaptureStoppedUnexpectedly;
            session.Stop();
            var finalText = session.GetFinalText();
            if (!string.IsNullOrWhiteSpace(finalText))
            {
                WriteMessage("final", finalText);
            }

            session.Dispose();
            WriteMessage("capture_stopped");
        }
        finally
        {
            CaptureLock.Release();
        }
    }

    private static void OnAudioReceived(object? sender, AudioReceivedEventArgs e)
    {
        if (sender is not CaptureSession session || !ReferenceEquals(_capture, session))
        {
            return;
        }

        if (e.IsFirstPacket)
        {
            WriteMessage("capture_started");
        }

        if (session.ShouldReportSpeechActivity(e.Audio))
        {
            WriteMessage("speech_activity");
        }

        var result = session.Recognize(e.Audio);
        if (result is not null)
        {
            WriteMessage(result.Type, result.Text, result.Message);
        }
    }

    private static void OnCaptureStoppedUnexpectedly(object? sender, CaptureErrorEventArgs e)
    {
        if (sender is not CaptureSession session || !ReferenceEquals(_capture, session))
        {
            return;
        }

        WriteMessage("error", message: e.Message);
        _ = Task.Run(StopCaptureAsync);
    }

    private static WorkerMessage? RecognizeAudio(
        VoskRecognizer recognizer,
        byte[] audio,
        ref string lastPartial)
    {
        if (recognizer.AcceptWaveform(audio, audio.Length))
        {
            var text = ReadText(recognizer.Result(), "text");
            lastPartial = string.Empty;
            return string.IsNullOrWhiteSpace(text)
                ? null
                : new WorkerMessage("final", text, null);
        }

        var partial = ReadText(recognizer.PartialResult(), "partial");
        if (string.IsNullOrWhiteSpace(partial) ||
            string.Equals(partial, lastPartial, StringComparison.Ordinal))
        {
            return null;
        }

        lastPartial = partial;
        return new WorkerMessage("partial", partial, null);
    }

    private static string ReadText(string json, string propertyName)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(propertyName, out var value)
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static void WriteMessage(string type, string? text = null, string? message = null)
    {
        var json = JsonSerializer.Serialize(new WorkerMessage(type, text, message));
        lock (OutputLock)
        {
            Console.Out.WriteLine(json);
            Console.Out.Flush();
        }
    }

    private sealed class CaptureSession : IDisposable
    {
        private readonly object _recognizerLock = new();
        private readonly WasapiCapture _waveIn = new();
        private readonly MicrophoneAudioConverter _converter;
        private readonly VoskRecognizer _recognizer;
        private string _lastPartial = string.Empty;
        private bool _firstPacket = true;
        private bool _stopping;
        private bool _disposed;
        private long _lastSpeechActivityTimestamp;

        public CaptureSession(Model model)
        {
            _recognizer = new VoskRecognizer(model, SampleRate);
            _converter = new MicrophoneAudioConverter(_waveIn.WaveFormat);
            _waveIn.DataAvailable += WaveInOnDataAvailable;
            _waveIn.RecordingStopped += WaveInOnRecordingStopped;
        }

        public event EventHandler<AudioReceivedEventArgs>? AudioReceived;
        public event EventHandler<CaptureErrorEventArgs>? StoppedUnexpectedly;

        public void Start() => _waveIn.StartRecording();

        public void Stop()
        {
            _stopping = true;
            try
            {
                _waveIn.StopRecording();
            }
            catch (InvalidOperationException)
            {
                // Capture was already stopped by the audio subsystem.
            }
        }

        public WorkerMessage? Recognize(byte[] audio)
        {
            lock (_recognizerLock)
            {
                if (_disposed)
                {
                    return null;
                }

                return RecognizeAudio(_recognizer, audio, ref _lastPartial);
            }
        }

        public bool ShouldReportSpeechActivity(byte[] audio)
        {
            if (!ContainsSpeech(audio))
            {
                return false;
            }

            var now = Stopwatch.GetTimestamp();
            var previous = Volatile.Read(ref _lastSpeechActivityTimestamp);
            if (now - previous < SpeechActivityIntervalTicks)
            {
                return false;
            }

            Volatile.Write(ref _lastSpeechActivityTimestamp, now);
            return true;
        }

        public string GetFinalText()
        {
            lock (_recognizerLock)
            {
                if (_disposed)
                {
                    return string.Empty;
                }

                return ReadText(_recognizer.FinalResult(), "text");
            }
        }

        private static bool ContainsSpeech(byte[] audio)
        {
            var sampleCount = audio.Length / sizeof(short);
            if (sampleCount == 0)
            {
                return false;
            }

            long squaredLevel = 0;
            for (var offset = 0; offset + 1 < audio.Length; offset += sizeof(short))
            {
                var sample = BitConverter.ToInt16(audio, offset);
                squaredLevel += (long)sample * sample;
            }

            return squaredLevel / sampleCount >=
                   (long)SpeechLevelThreshold * SpeechLevelThreshold;
        }

        private void WaveInOnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (_stopping) return;

            foreach (var audio in _converter.Convert(e.Buffer, e.BytesRecorded))
            {
                var isFirst = _firstPacket;
                _firstPacket = false;
                AudioReceived?.Invoke(this, new AudioReceivedEventArgs(audio, isFirst));
            }
        }

        private void WaveInOnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (!_stopping)
            {
                StoppedUnexpectedly?.Invoke(
                    this,
                    new CaptureErrorEventArgs(
                        e.Exception is null
                            ? "麦克风录音意外停止。"
                            : $"麦克风录音意外停止：{e.Exception.Message}"));
            }
        }

        public void Dispose()
        {
            lock (_recognizerLock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            _waveIn.DataAvailable -= WaveInOnDataAvailable;
            _waveIn.RecordingStopped -= WaveInOnRecordingStopped;
            _waveIn.Dispose();
            lock (_recognizerLock)
            {
                _recognizer.Dispose();
            }
        }
    }

    private sealed class MicrophoneAudioConverter
    {
        private readonly BufferedWaveProvider _bufferedInput;
        private readonly IWaveProvider _pcmOutput;
        private readonly byte[] _outputBuffer = new byte[8192];

        public MicrophoneAudioConverter(WaveFormat inputFormat)
        {
            _bufferedInput = new BufferedWaveProvider(inputFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(2),
                DiscardOnBufferOverflow = true,
                ReadFully = false
            };

            ISampleProvider samples = _bufferedInput.ToSampleProvider();
            if (samples.WaveFormat.Channels != 1)
            {
                samples = new DownmixToMonoSampleProvider(samples);
            }

            if (samples.WaveFormat.SampleRate != SampleRate)
            {
                samples = new WdlResamplingSampleProvider(samples, SampleRate);
            }

            _pcmOutput = new SampleToWaveProvider16(samples);
        }

        public IReadOnlyList<byte[]> Convert(byte[] input, int count)
        {
            _bufferedInput.AddSamples(input, 0, count);
            var converted = new List<byte[]>();
            while (true)
            {
                var bytesRead = _pcmOutput.Read(_outputBuffer, 0, _outputBuffer.Length);
                if (bytesRead <= 0) break;

                var audio = new byte[bytesRead];
                Buffer.BlockCopy(_outputBuffer, 0, audio, 0, bytesRead);
                converted.Add(audio);
                if (bytesRead < _outputBuffer.Length) break;
            }

            return converted;
        }
    }

    private sealed class DownmixToMonoSampleProvider(ISampleProvider source) : ISampleProvider
    {
        private readonly int _sourceChannels = source.WaveFormat.Channels;
        private float[] _sourceBuffer = [];

        public WaveFormat WaveFormat { get; } =
            WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            var requiredSamples = checked(count * _sourceChannels);
            if (_sourceBuffer.Length < requiredSamples)
            {
                _sourceBuffer = new float[requiredSamples];
            }

            var sourceSamplesRead = source.Read(_sourceBuffer, 0, requiredSamples);
            var framesRead = sourceSamplesRead / _sourceChannels;
            for (var frame = 0; frame < framesRead; frame++)
            {
                float sum = 0;
                var sourceOffset = frame * _sourceChannels;
                for (var channel = 0; channel < _sourceChannels; channel++)
                {
                    sum += _sourceBuffer[sourceOffset + channel];
                }

                buffer[offset + frame] = sum / _sourceChannels;
            }

            return framesRead;
        }
    }

    private sealed class AudioReceivedEventArgs(byte[] audio, bool isFirstPacket) : EventArgs
    {
        public byte[] Audio { get; } = audio;
        public bool IsFirstPacket { get; } = isFirstPacket;
    }

    private sealed class CaptureErrorEventArgs(string message) : EventArgs
    {
        public string Message { get; } = message;
    }
    private sealed record WorkerMessage(string Type, string? Text, string? Message);
}
