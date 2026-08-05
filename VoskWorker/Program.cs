using System.Diagnostics;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SystemTools.VoskWorker;

internal static class Program
{
    private const int SampleRate = 16000;
    private const int SpeechLevelThreshold = 450;
    private static readonly long SpeechActivityIntervalTicks = Stopwatch.Frequency / 5;
    private static readonly object OutputLock = new();
    private static readonly object PendingStopTasksLock = new();
    private static readonly SemaphoreSlim CaptureLock = new(1, 1);
    private static readonly HashSet<Task> PendingStopTasks = [];
    private static ISpeechRecognitionModel? _model;
    private static CaptureSession? _capture;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length != 1 || !Directory.Exists(args[0]))
            {
                WriteMessage("error", message: "语音识别模型目录无效。");
                return 2;
            }

            _model = await Task.Run(() => SpeechRecognitionModelFactory.Load(args[0]));
            WriteMessage("model_ready");
            await MonitorParentCommandsAsync();
            await StopCurrentCaptureAsync();
            return 0;
        }
        catch (Exception ex)
        {
            WriteMessage("error", message: $"语音识别工作进程失败：{ex.Message}");
            return 1;
        }
        finally
        {
            await StopCurrentCaptureAsync();
            await WaitForPendingStopTasksAsync();

            _model?.Dispose();
            _model = null;
        }
    }

    private static async Task MonitorParentCommandsAsync()
    {
        while (true)
        {
            var commandLine = await Console.In.ReadLineAsync();
            if (commandLine is null)
            {
                return;
            }

            var command = ParseCommand(commandLine);
            if (string.Equals(command.Type, "shutdown", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(command.Type, "stop", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.Equals(command.Type, "start_capture", StringComparison.OrdinalIgnoreCase))
            {
                await StartCaptureAsync(command.CaptureId);
            }
            else if (string.Equals(command.Type, "stop_capture", StringComparison.OrdinalIgnoreCase))
            {
                _ = QueueStopCapture(command.CaptureId, CaptureStopMode.Finish);
            }
            else if (string.Equals(command.Type, "cancel_capture", StringComparison.OrdinalIgnoreCase))
            {
                _ = QueueStopCapture(command.CaptureId, CaptureStopMode.Discard);
            }
        }
    }

    private static async Task StartCaptureAsync(long captureId)
    {
        await CaptureLock.WaitAsync();
        try
        {
            var activeCapture = Volatile.Read(ref _capture);
            if (activeCapture is not null)
            {
                if (activeCapture.Id == captureId)
                {
                    WriteMessage("capture_started", captureId: captureId);
                }
                else
                {
                    WriteMessage(
                        "error",
                        message: "另一个麦克风采集轮次仍在结束。",
                        captureId: captureId);
                }
                return;
            }

            if (_model is null || captureId <= 0)
            {
                WriteMessage(
                    "error",
                    message: _model is null
                        ? "语音识别模型尚未加载。"
                        : "麦克风采集轮次 ID 无效。",
                    captureId: captureId > 0 ? captureId : null);
                return;
            }

            var session = new CaptureSession(captureId, _model.CreateSession());
            Volatile.Write(ref _capture, session);
            session.AudioReceived += OnAudioReceived;
            session.StoppedUnexpectedly += OnCaptureStoppedUnexpectedly;
            session.Start();
        }
        catch (Exception ex)
        {
            var failedCapture = Interlocked.Exchange(ref _capture, null);
            failedCapture?.Dispose();
            WriteMessage(
                "error",
                message: $"无法打开麦克风：{ex.Message}",
                captureId: captureId);
        }
        finally
        {
            CaptureLock.Release();
        }
    }

    private static Task QueueStopCapture(long captureId, CaptureStopMode stopMode)
    {
        var session = Volatile.Read(ref _capture);
        if (session is null || session.Id != captureId)
        {
            if (captureId > 0)
            {
                WriteMessage("capture_stopped", captureId: captureId);
            }

            return Task.CompletedTask;
        }

        var stopRequest = session.RequestStop(stopMode);
        if (!stopRequest.ShouldStart)
        {
            return stopRequest.Completion.Task;
        }

        TrackStopTask(stopRequest.Completion.Task);
        _ = RunStopCaptureCoreAsync(session, stopRequest.Completion);
        return stopRequest.Completion.Task;
    }

    private static async Task RunStopCaptureCoreAsync(
        CaptureSession session,
        TaskCompletionSource<bool> completion)
    {
        try
        {
            await StopCaptureCoreAsync(session);
            completion.TrySetResult(true);
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    private static async Task StopCaptureCoreAsync(CaptureSession session)
    {
        await CaptureLock.WaitAsync();
        var captureLockHeld = true;
        try
        {
            if (!ReferenceEquals(Volatile.Read(ref _capture), session))
            {
                return;
            }

            session.AudioReceived -= OnAudioReceived;
            session.StoppedUnexpectedly -= OnCaptureStoppedUnexpectedly;
            await session.StopAndDrainAsync();

            var partialTask = session.WaitForPartialRecognitionAsync();
            if (await DiscardRequestedBeforeCompletionAsync(session, partialTask))
            {
                // WASAPI callbacks are drained, so the new capture can start while
                // any already-running model inference finishes before disposal.
                session.TryCommitDiscard();
                CompleteCapture(session);
                CaptureLock.Release();
                captureLockHeld = false;
                await IgnoreDiscardedRecognitionAsync(partialTask);
                return;
            }

            await partialTask;
            if (session.ShouldDiscardResults)
            {
                session.TryCommitDiscard();
                CompleteCapture(session);
                return;
            }

            var finalTask = session.GetFinalTextAsync();
            if (await DiscardRequestedBeforeCompletionAsync(session, finalTask))
            {
                session.TryCommitDiscard();
                CompleteCapture(session);
                CaptureLock.Release();
                captureLockHeld = false;
                await IgnoreDiscardedRecognitionAsync(finalTask);
                return;
            }

            var finalText = await finalTask;
            if (session.TryCommitFinish())
            {
                if (!string.IsNullOrWhiteSpace(finalText))
                {
                    WriteMessage("final", finalText, captureId: session.Id);
                }
            }
            else
            {
                session.TryCommitDiscard();
            }

            CompleteCapture(session);
        }
        catch (Exception ex)
        {
            Interlocked.CompareExchange(ref _capture, null, session);
            WriteMessage(
                "error",
                message: $"结束语音识别失败：{ex.Message}",
                captureId: session.Id);
            WriteMessage("capture_stopped", captureId: session.Id);
        }
        finally
        {
            try
            {
                session.Dispose();
            }
            finally
            {
                if (captureLockHeld)
                {
                    CaptureLock.Release();
                }
            }
        }
    }

    private static async Task<bool> DiscardRequestedBeforeCompletionAsync(
        CaptureSession session,
        Task recognitionTask)
    {
        if (session.ShouldDiscardResults)
        {
            return true;
        }

        await Task.WhenAny(recognitionTask, session.DiscardRequested);
        return session.ShouldDiscardResults;
    }

    private static async Task IgnoreDiscardedRecognitionAsync(Task recognitionTask)
    {
        try
        {
            await recognitionTask;
        }
        catch
        {
            // The capture was already acknowledged as discarded.
        }
    }

    private static void CompleteCapture(CaptureSession session)
    {
        Interlocked.CompareExchange(ref _capture, null, session);
        WriteMessage("capture_stopped", captureId: session.Id);
    }

    private static async Task StopCurrentCaptureAsync()
    {
        var session = Volatile.Read(ref _capture);
        if (session is not null)
        {
            await QueueStopCapture(session.Id, CaptureStopMode.Finish);
        }

        await WaitForPendingStopTasksAsync();
    }

    private static void TrackStopTask(Task task)
    {
        lock (PendingStopTasksLock)
        {
            PendingStopTasks.Add(task);
        }

        _ = task.ContinueWith(
            completedTask =>
            {
                _ = completedTask.Exception;
                lock (PendingStopTasksLock)
                {
                    PendingStopTasks.Remove(completedTask);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static async Task WaitForPendingStopTasksAsync()
    {
        while (true)
        {
            Task[] pendingTasks;
            lock (PendingStopTasksLock)
            {
                pendingTasks = [.. PendingStopTasks];
            }

            if (pendingTasks.Length == 0)
            {
                return;
            }

            await Task.WhenAll(pendingTasks);
        }
    }

    private static void OnAudioReceived(object? sender, AudioReceivedEventArgs e)
    {
        if (sender is not CaptureSession session ||
            !ReferenceEquals(Volatile.Read(ref _capture), session))
        {
            return;
        }

        if (e.IsFirstPacket)
        {
            WriteMessage("capture_started", captureId: session.Id);
        }

        WriteMessage(
            "audio_level",
            level: session.GetAudioLevel(e.Audio),
            captureId: session.Id);

        if (session.ShouldReportSpeechActivity(e.Audio))
        {
            WriteMessage("speech_activity", captureId: session.Id);
        }

        var result = session.Recognize(e.Audio);
        if (result is not null)
        {
            WriteMessage(
                result.Type,
                result.Text,
                result.Message,
                captureId: session.Id);
        }

        session.TryStartPartialRecognition(() => PublishPartialRecognitionAsync(session));
    }

    private static async Task PublishPartialRecognitionAsync(CaptureSession session)
    {
        try
        {
            var text = await session.GetPartialTextAsync();
            if (!string.IsNullOrWhiteSpace(text) &&
                ReferenceEquals(Volatile.Read(ref _capture), session) &&
                !session.ShouldDiscardResults)
            {
                WriteMessage("partial", text, captureId: session.Id);
            }
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(Volatile.Read(ref _capture), session))
            {
                WriteMessage(
                    "error",
                    message: $"语音识别失败：{ex.Message}",
                    captureId: session.Id);
                _ = QueueStopCapture(session.Id, CaptureStopMode.Finish);
            }
        }
    }

    private static void OnCaptureStoppedUnexpectedly(object? sender, CaptureErrorEventArgs e)
    {
        if (sender is not CaptureSession session ||
            !ReferenceEquals(Volatile.Read(ref _capture), session))
        {
            return;
        }

        WriteMessage("error", message: e.Message, captureId: session.Id);
        _ = QueueStopCapture(session.Id, CaptureStopMode.Finish);
    }

    private static void WriteMessage(
        string type,
        string? text = null,
        string? message = null,
        double? level = null,
        long? captureId = null)
    {
        var json = JsonSerializer.Serialize(
            new WorkerOutputMessage(type, text, message, level, captureId));
        lock (OutputLock)
        {
            Console.Out.WriteLine(json);
            Console.Out.Flush();
        }
    }

    private static WorkerCommand ParseCommand(string commandLine)
    {
        try
        {
            var command = JsonSerializer.Deserialize<WorkerCommand>(commandLine);
            if (command is not null && !string.IsNullOrWhiteSpace(command.Type))
            {
                return command;
            }
        }
        catch (JsonException)
        {
            // Shutdown from older hosts is a plain command line.
        }

        return new WorkerCommand(commandLine, 0);
    }

    private enum CaptureStopMode
    {
        None,
        Finish,
        Discard
    }

    private sealed record WorkerCommand(string Type, long CaptureId);

    private sealed record WorkerOutputMessage(
        string Type,
        string? Text,
        string? Message,
        double? Level,
        long? CaptureId);

    private sealed class CaptureSession : IDisposable
    {
        private readonly object _callbackGate = new();
        private readonly object _recognizerLock = new();
        private readonly object _partialTaskLock = new();
        private readonly object _stopStateLock = new();
        private readonly TaskCompletionSource<bool> _discardRequested = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _recordingStopped = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly WasapiCapture _waveIn = new();
        private readonly MicrophoneAudioConverter _converter;
        private readonly IRecognitionSession _recognizer;
        private TaskCompletionSource<bool>? _callbacksDrained;
        private TaskCompletionSource<bool>? _stopCompletion;
        private Task _partialRecognitionTask = Task.CompletedTask;
        private string _lastDeferredPartial = string.Empty;
        private CaptureStopMode _stopMode;
        private int _activeAudioCallbacks;
        private bool _acceptAudioCallbacks = true;
        private bool _completionCommitted;
        private bool _firstPacket = true;
        private bool _stopStarted;
        private volatile bool _stopping;
        private bool _disposed;
        private long _lastSpeechActivityTimestamp;

        public CaptureSession(long id, IRecognitionSession recognizer)
        {
            Id = id;
            _recognizer = recognizer;
            _converter = new MicrophoneAudioConverter(_waveIn.WaveFormat);
            _waveIn.DataAvailable += WaveInOnDataAvailable;
            _waveIn.RecordingStopped += WaveInOnRecordingStopped;
        }

        public event EventHandler<AudioReceivedEventArgs>? AudioReceived;
        public event EventHandler<CaptureErrorEventArgs>? StoppedUnexpectedly;
        public long Id { get; }
        public Task DiscardRequested => _discardRequested.Task;

        public bool ShouldDiscardResults
        {
            get
            {
                lock (_stopStateLock)
                {
                    return _stopMode == CaptureStopMode.Discard;
                }
            }
        }

        public void Start() => _waveIn.StartRecording();

        public StopRequest RequestStop(CaptureStopMode stopMode)
        {
            var signalDiscard = false;
            bool shouldStart;
            TaskCompletionSource<bool> completion;
            lock (_stopStateLock)
            {
                if (_completionCommitted)
                {
                    return new StopRequest(
                        false,
                        _stopCompletion ?? throw new InvalidOperationException(
                            "Capture completion was committed without a stop task."));
                }

                if (stopMode > _stopMode)
                {
                    _stopMode = stopMode;
                    signalDiscard = stopMode == CaptureStopMode.Discard;
                }

                shouldStart = !_stopStarted;
                _stopStarted = true;
                completion = _stopCompletion ??= new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }

            if (signalDiscard)
            {
                _discardRequested.TrySetResult(true);
            }

            return new StopRequest(shouldStart, completion);
        }

        public bool TryCommitFinish()
        {
            lock (_stopStateLock)
            {
                if (_completionCommitted || _stopMode == CaptureStopMode.Discard)
                {
                    return false;
                }

                _completionCommitted = true;
                return true;
            }
        }

        public bool TryCommitDiscard()
        {
            lock (_stopStateLock)
            {
                if (_completionCommitted)
                {
                    return false;
                }

                _stopMode = CaptureStopMode.Discard;
                _completionCommitted = true;
            }

            _discardRequested.TrySetResult(true);
            return true;
        }

        public async Task StopAndDrainAsync()
        {
            Task callbacksDrained;
            lock (_callbackGate)
            {
                _acceptAudioCallbacks = false;
                callbacksDrained = _activeAudioCallbacks == 0
                    ? Task.CompletedTask
                    : (_callbacksDrained ??= new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            }

            _stopping = true;
            Exception? stopError = null;
            try
            {
                _waveIn.StopRecording();
            }
            catch (Exception ex)
            {
                stopError = ex;
                _recordingStopped.TrySetResult(true);
            }

            await _recordingStopped.Task;
            await callbacksDrained;
            if (stopError is not null && stopError is not InvalidOperationException)
            {
                throw stopError;
            }
        }

        public WorkerMessage? Recognize(byte[] audio)
        {
            lock (_recognizerLock)
            {
                return _disposed ? null : _recognizer.AcceptAudio(audio);
            }
        }

        public void TryStartPartialRecognition(Func<Task> recognition)
        {
            lock (_partialTaskLock)
            {
                if (_disposed || !_partialRecognitionTask.IsCompleted ||
                    !_recognizer.TryBeginPartialRecognition())
                {
                    return;
                }

                _partialRecognitionTask = Task.Run(recognition);
            }
        }

        public async Task<string?> GetPartialTextAsync()
        {
            var text = await _recognizer.GetPartialTextAsync();
            if (string.IsNullOrWhiteSpace(text) ||
                string.Equals(text, _lastDeferredPartial, StringComparison.Ordinal))
            {
                return null;
            }

            _lastDeferredPartial = text;
            return text;
        }

        public Task WaitForPartialRecognitionAsync()
        {
            lock (_partialTaskLock)
            {
                return _partialRecognitionTask;
            }
        }

        public Task<string> GetFinalTextAsync()
        {
            lock (_recognizerLock)
            {
                return _disposed
                    ? Task.FromResult(string.Empty)
                    : _recognizer.GetFinalTextAsync();
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

        public double GetAudioLevel(byte[] audio)
        {
            var sampleCount = audio.Length / sizeof(short);
            if (sampleCount == 0)
            {
                return 0;
            }

            double squaredLevel = 0;
            for (var offset = 0; offset + 1 < audio.Length; offset += sizeof(short))
            {
                var sample = BitConverter.ToInt16(audio, offset);
                squaredLevel += (double)sample * sample;
            }

            var rms = Math.Sqrt(squaredLevel / sampleCount);
            return Math.Clamp((rms - SpeechLevelThreshold * 0.35) / 6000d, 0, 1);
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
            if (!TryEnterAudioCallback())
            {
                return;
            }

            try
            {
                foreach (var audio in _converter.Convert(e.Buffer, e.BytesRecorded))
                {
                    var isFirst = _firstPacket;
                    _firstPacket = false;
                    AudioReceived?.Invoke(this, new AudioReceivedEventArgs(audio, isFirst));
                }
            }
            finally
            {
                ExitAudioCallback();
            }
        }

        private void WaveInOnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            _recordingStopped.TrySetResult(true);
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

        private bool TryEnterAudioCallback()
        {
            lock (_callbackGate)
            {
                if (!_acceptAudioCallbacks)
                {
                    return false;
                }

                _activeAudioCallbacks++;
                return true;
            }
        }

        private void ExitAudioCallback()
        {
            TaskCompletionSource<bool>? callbacksDrained = null;
            lock (_callbackGate)
            {
                _activeAudioCallbacks--;
                if (!_acceptAudioCallbacks && _activeAudioCallbacks == 0)
                {
                    callbacksDrained = _callbacksDrained;
                }
            }

            callbacksDrained?.TrySetResult(true);
        }

        public void Dispose()
        {
            lock (_callbackGate)
            {
                _acceptAudioCallbacks = false;
            }

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

    private readonly record struct StopRequest(
        bool ShouldStart,
        TaskCompletionSource<bool> Completion);

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
                if (bytesRead <= 0)
                {
                    break;
                }

                var audio = new byte[bytesRead];
                Buffer.BlockCopy(_outputBuffer, 0, audio, 0, bytesRead);
                converted.Add(audio);
                if (bytesRead < _outputBuffer.Length)
                {
                    break;
                }
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
}
