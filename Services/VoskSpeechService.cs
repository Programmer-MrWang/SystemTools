using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SystemTools.Shared;

namespace SystemTools.Services;

/// <summary>
/// Owns one persistent Vosk worker. The worker keeps the model loaded while
/// individual capture leases open and close the microphone for each turn.
/// </summary>
public sealed class VoskSpeechService(ILogger<VoskSpeechService> logger) : IDisposable
{
    private const int WorkerExitTimeoutMilliseconds = 3000;
    private static readonly TimeSpan ModelStartupTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CaptureStartupTimeout = TimeSpan.FromSeconds(5);
    private readonly ILogger<VoskSpeechService> _logger = logger;
    private readonly object _lock = new();
    private readonly object _stdinLock = new();
    private WorkerSession? _worker;
    private CaptureSession? _capture;
    private object? _exclusiveOwner;
    private int _modelLeaseCount;
    private int _workerWaiterCount;
    private bool _disposed;

    internal sealed class WorkerSession
    {
        public required Process Process { get; init; }
        public TaskCompletionSource<bool> Ready { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class CaptureSession
    {
        public required WorkerSession Worker { get; init; }
        public required Action<string, bool> OnText { get; init; }
        public required Action<string> OnError { get; init; }
        public Action? OnSpeechActivity { get; init; }
        public Action<double>? OnAudioLevel { get; init; }
        public object? Owner { get; init; }
        public TaskCompletionSource<bool> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Stopped { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool StopRequested { get; set; }
    }

    private sealed record WorkerMessage(string? Type, string? Text, string? Message, double? Level);

    public bool IsDictationActive
    {
        get
        {
            lock (_lock)
            {
                return _capture is not null || _exclusiveOwner is not null;
            }
        }
    }

    public bool IsModelLoaded
    {
        get
        {
            lock (_lock)
            {
                return _worker is not null;
            }
        }
    }

    public event EventHandler? DictationStateChanged;

    /// <summary>Loads the model without opening the microphone.</summary>
    public async Task<IDisposable?> TryAcquireModelAsync(
        Action<string> onError,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onError);

        if (!OperatingSystem.IsWindows())
        {
            onError("Vosk 语音输入仅支持 Windows 麦克风。");
            return null;
        }

        bool isReserved;
        lock (_lock)
        {
            isReserved = _exclusiveOwner is not null;
        }

        if (isReserved)
        {
            onError("语音对话正在使用 Vosk 语音识别。");
            return null;
        }

        var worker = await EnsureWorkerAsync(onError, cancellationToken);
        if (worker is null)
        {
            return null;
        }

        lock (_lock)
        {
            if (_disposed ||
                _exclusiveOwner is not null ||
                !ReferenceEquals(_worker, worker))
            {
                return null;
            }

            _modelLeaseCount++;
        }

        return new ModelLease(this, worker);
    }

    /// <summary>
    /// Reserves Vosk for a continuous conversation. Other AI chat windows cannot
    /// open the microphone while this session keeps the model warm.
    /// </summary>
    public async Task<ConversationSession?> TryAcquireConversationAsync(
        Action<string> onError,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onError);

        if (!OperatingSystem.IsWindows())
        {
            onError("Vosk 语音输入仅支持 Windows 麦克风。");
            return null;
        }

        var owner = new object();
        bool isBusy;
        lock (_lock)
        {
            isBusy = _disposed ||
                     _exclusiveOwner is not null ||
                     _capture is not null ||
                     _modelLeaseCount > 0;
            if (!isBusy)
            {
                _exclusiveOwner = owner;
                // Hold a provisional model lease for the entire asynchronous
                // startup. A normal model lease released while the worker is
                // loading must not tear down the worker reserved by this
                // conversation.
                _modelLeaseCount++;
            }
        }

        if (isBusy)
        {
            onError("另一个 AI 对话窗口正在使用语音输入，请先关闭该语音输入。");
            return null;
        }

        RaiseDictationStateChanged();
        var acquired = false;
        try
        {
            var worker = await EnsureWorkerAsync(onError, cancellationToken);
            if (worker is null)
            {
                return null;
            }

            lock (_lock)
            {
                if (_disposed ||
                    !ReferenceEquals(_exclusiveOwner, owner) ||
                    !ReferenceEquals(_worker, worker))
                {
                    return null;
                }

            }

            acquired = true;
            return new ConversationSession(this, worker, owner);
        }
        finally
        {
            if (!acquired)
            {
                ReleaseConversationReservation(owner);
            }
        }
    }

    /// <summary>Starts one microphone capture using an already loaded model.</summary>
    public async Task<IDisposable?> TryStartCaptureAsync(
        Action<string, bool> onText,
        Action<string> onError,
        CancellationToken cancellationToken = default)
        => await TryStartCaptureCoreAsync(
            onText,
            onError,
            onSpeechActivity: null,
            onAudioLevel: null,
            owner: null,
            cancellationToken);

    public async Task<IDisposable?> TryStartCaptureAsync(
        Action<string, bool> onText,
        Action<string> onError,
        Action<double> onAudioLevel,
        CancellationToken cancellationToken = default)
        => await TryStartCaptureCoreAsync(
            onText,
            onError,
            onSpeechActivity: null,
            onAudioLevel: onAudioLevel,
            owner: null,
            cancellationToken);

    private async Task<IDisposable?> TryStartCaptureCoreAsync(
        Action<string, bool> onText,
        Action<string> onError,
        Action? onSpeechActivity,
        Action<double>? onAudioLevel,
        object? owner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onText);
        ArgumentNullException.ThrowIfNull(onError);

        WorkerSession? worker;
        CaptureSession capture;
        lock (_lock)
        {
            worker = _worker;
            var ownerCanCapture = owner is null
                ? _exclusiveOwner is null
                : ReferenceEquals(_exclusiveOwner, owner);
            if (_disposed ||
                worker is null ||
                _capture is not null ||
                !ownerCanCapture)
            {
                return null;
            }

            capture = new CaptureSession
            {
                Worker = worker,
                OnText = onText,
                OnError = onError,
                OnSpeechActivity = onSpeechActivity,
                OnAudioLevel = onAudioLevel,
                Owner = owner
            };
            _capture = capture;
        }

        try
        {
            if (!SendCommand(worker, "start_capture"))
            {
                onError("Vosk 工作进程已不可用。");
                await StopCaptureAsync(capture, owner);
                return null;
            }

            var started = await capture.Started.Task.WaitAsync(
                CaptureStartupTimeout,
                cancellationToken);
            if (!started)
            {
                await StopCaptureAsync(capture, owner);
                return null;
            }

            RaiseDictationStateChanged();
            return new CaptureLease(this, capture);
        }
        catch (TimeoutException)
        {
            onError("麦克风已打开，但 5 秒内没有收到音频数据。请检查默认输入设备和麦克风权限。");
            await StopCaptureAsync(capture, owner);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await StopCaptureAsync(capture, owner);
            return null;
        }
        catch (Exception ex)
        {
            onError($"无法启动 Vosk 麦克风：{ex.Message}");
            await StopCaptureAsync(capture, owner);
            return null;
        }
    }

    /// <summary>
    /// Compatibility wrapper used by the existing AI chat voice input.
    /// Disposing the returned lease stops capture and releases the model.
    /// </summary>
    public async Task<IDisposable?> TryStartDictationAsync(
        Action<string, bool> onText,
        Action<string> onError,
        string? context = null,
        CancellationToken cancellationToken = default)
    {
        _ = context;
        var modelLease = await TryAcquireModelAsync(onError, cancellationToken);
        if (modelLease is null)
        {
            return null;
        }

        var captureLease = await TryStartCaptureAsync(onText, onError, cancellationToken);
        if (captureLease is null)
        {
            modelLease.Dispose();
            return null;
        }

        return new DictationLease(this, captureLease, modelLease);
    }

    public Task StopCaptureAsync() => StopCaptureAsync(expected: null, owner: null);

    private async Task StopCaptureAsync(CaptureSession? expected, object? owner)
    {
        CaptureSession? capture;
        WorkerSession? worker;
        bool stopAlreadyRequested;
        lock (_lock)
        {
            capture = _capture;
            if (capture is null ||
                (expected is not null && !ReferenceEquals(capture, expected)) ||
                (owner is not null && !ReferenceEquals(capture.Owner, owner)))
            {
                return;
            }

            stopAlreadyRequested = capture.StopRequested;
            capture.StopRequested = true;
            worker = capture.Worker;
        }

        try
        {
            if (stopAlreadyRequested)
            {
                await capture.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
                return;
            }

            if (!SendCommand(worker, "stop_capture"))
            {
                capture.Stopped.TrySetResult(true);
                ClearCapture(capture);
                RaiseDictationStateChanged();
                return;
            }

            await capture.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[VoskSpeech] Capture stop acknowledgement failed");
            StopWorker(worker);
        }
    }

    private async Task<WorkerSession?> EnsureWorkerAsync(
        Action<string> onError,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _workerWaiterCount);
        try
        {
            return await EnsureWorkerCoreAsync(onError, cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _workerWaiterCount);
        }
    }

    private async Task<WorkerSession?> EnsureWorkerCoreAsync(
        Action<string> onError,
        CancellationToken cancellationToken)
    {
        WorkerSession? existing;
        lock (_lock)
        {
            existing = _worker;
        }

        if (existing is not null)
        {
            try
            {
                var ready = await WaitForWorkerReadyAsync(existing, onError, cancellationToken);
                if (ready is null)
                {
                    StopFailedWorkerIfUnused(existing);
                }

                return ready;
            }
            catch
            {
                StopFailedWorkerIfUnused(existing);
                throw;
            }
        }

        var dependencyCheck = DependencyPaths.CheckVoskDependencies();
        if (!dependencyCheck.IsAvailable)
        {
            onError(dependencyCheck.Message);
            return null;
        }

        var modelPath = DependencyPaths.FindVoskModelDirectory();
        var workerPath = DependencyPaths.FindVoskWorkerPath();
        if (modelPath is null || workerPath is null)
        {
            onError("Vosk 模型或工作进程不存在。");
            return null;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = workerPath,
            WorkingDirectory = Path.GetDirectoryName(workerPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(modelPath);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var session = new WorkerSession { Process = process };
        WorkerSession? concurrentWorker = null;
        lock (_lock)
        {
            if (_disposed)
            {
                process.Dispose();
                return null;
            }

            if (_worker is not null)
            {
                concurrentWorker = _worker;
            }
            else
            {
                _worker = session;
            }
        }

        if (concurrentWorker is not null)
        {
            process.Dispose();
            try
            {
                var ready = await WaitForWorkerReadyAsync(
                    concurrentWorker,
                    onError,
                    cancellationToken);
                if (ready is null)
                {
                    StopFailedWorkerIfUnused(concurrentWorker);
                }

                return ready;
            }
            catch
            {
                StopFailedWorkerIfUnused(concurrentWorker);
                throw;
            }
        }

        process.OutputDataReceived += OnWorkerOutput;
        process.ErrorDataReceived += OnWorkerErrorOutput;
        process.Exited += OnWorkerExited;

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Vosk 工作进程未能启动。");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!await session.Ready.Task.WaitAsync(ModelStartupTimeout, cancellationToken))
            {
                throw new InvalidOperationException("Vosk 模型加载失败。");
            }

            _logger.LogInformation("[VoskSpeech] Model loaded (PID: {ProcessId})", process.Id);
            return session;
        }
        catch (TimeoutException)
        {
            onError("Vosk 语音识别模型加载超时，请检查模型文件是否完整。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller cancelled startup.
        }
        catch (Exception ex)
        {
            onError($"无法加载 Vosk 语音识别模型：{ex.Message}");
        }

        // Wake any concurrent callers waiting on this startup attempt before
        // deciding whether the failed process can be torn down here.
        session.Ready.TrySetResult(false);
        bool shouldStop;
        lock (_lock)
        {
            shouldStop = ReferenceEquals(_worker, session) &&
                         Volatile.Read(ref _workerWaiterCount) <= 1 &&
                         _modelLeaseCount == 0 &&
                         _exclusiveOwner is null &&
                         _capture is null;
        }

        if (shouldStop)
        {
            StopWorker(session);
        }
        return null;
    }

    private void StopFailedWorkerIfUnused(WorkerSession expected)
    {
        bool shouldStop;
        lock (_lock)
        {
            shouldStop = ReferenceEquals(_worker, expected) &&
                         Volatile.Read(ref _workerWaiterCount) <= 1 &&
                         _modelLeaseCount == 0 &&
                         _capture is null &&
                         _exclusiveOwner is null;
        }

        if (shouldStop)
        {
            StopWorker(expected);
        }
    }

    private static async Task<WorkerSession?> WaitForWorkerReadyAsync(
        WorkerSession worker,
        Action<string> onError,
        CancellationToken cancellationToken)
    {
        try
        {
            return await worker.Ready.Task.WaitAsync(ModelStartupTimeout, cancellationToken)
                ? worker
                : null;
        }
        catch (TimeoutException)
        {
            onError("Vosk 语音识别模型加载超时，请检查模型文件是否完整。");
            return null;
        }
    }

    private void OnWorkerOutput(object sender, DataReceivedEventArgs e)
    {
        if (sender is not Process process || string.IsNullOrWhiteSpace(e.Data)) return;

        WorkerSession? worker;
        lock (_lock)
        {
            worker = _worker;
            if (worker is null || !ReferenceEquals(worker.Process, process)) return;
        }

        WorkerMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<WorkerMessage>(e.Data);
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "[VoskSpeech] Invalid worker output: {Output}", e.Data);
            return;
        }

        CaptureSession? capture;
        lock (_lock)
        {
            capture = _capture;
            if (capture is not null && !ReferenceEquals(capture.Worker, worker))
            {
                capture = null;
            }
        }

        switch (message?.Type)
        {
            case "model_ready":
                worker.Ready.TrySetResult(true);
                break;
            case "capture_started":
                capture?.Started.TrySetResult(true);
                break;
            case "partial" when capture is not null && !string.IsNullOrWhiteSpace(message.Text):
                SafeInvokeText(capture, message.Text!, false);
                break;
            case "speech_activity" when capture is not null:
                SafeInvokeSpeechActivity(capture);
                break;
            case "audio_level" when capture is not null:
                SafeInvokeAudioLevel(capture, message.Level ?? 0);
                break;
            case "final" when capture is not null && !string.IsNullOrWhiteSpace(message.Text):
                SafeInvokeText(capture, message.Text!, true);
                break;
            case "capture_stopped":
                if (capture is not null)
                {
                    ClearCapture(capture);
                    capture.Stopped.TrySetResult(true);
                    RaiseDictationStateChanged();
                }
                break;
            case "error":
                var error = string.IsNullOrWhiteSpace(message.Message)
                    ? "Vosk 工作进程发生未知错误。"
                    : message.Message!;
                worker.Ready.TrySetResult(false);
                capture?.Started.TrySetResult(false);
                capture?.Stopped.TrySetResult(true);
                SafeInvokeError(capture, error);
                StopWorker(worker);
                break;
        }
    }

    private void SafeInvokeText(CaptureSession capture, string text, bool isFinal)
    {
        try
        {
            capture.OnText(text, isFinal);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[VoskSpeech] Recognition callback failed");
        }
    }

    private void SafeInvokeSpeechActivity(CaptureSession capture)
    {
        try
        {
            capture.OnSpeechActivity?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[VoskSpeech] Speech activity callback failed");
        }
    }

    private void SafeInvokeAudioLevel(CaptureSession capture, double level)
    {
        try
        {
            capture.OnAudioLevel?.Invoke(level);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[VoskSpeech] Audio level callback failed");
        }
    }

    private void SafeInvokeError(CaptureSession? capture, string message)
    {
        try
        {
            capture?.OnError(message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[VoskSpeech] Error callback failed");
        }
    }

    private void RaiseDictationStateChanged()
    {
        var handlers = DictationStateChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[VoskSpeech] State change subscriber failed");
            }
        }
    }

    private void OnWorkerErrorOutput(object sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.Data))
        {
            _logger.LogDebug("[VoskSpeech.Worker] {Message}", e.Data);
        }
    }

    private void OnWorkerExited(object? sender, EventArgs e)
    {
        if (sender is not Process process) return;

        WorkerSession? worker;
        CaptureSession? capture;
        lock (_lock)
        {
            worker = _worker;
            if (worker is null || !ReferenceEquals(worker.Process, process)) return;
            _worker = null;
            capture = _capture;
            _capture = null;
        }

        worker.Ready.TrySetResult(false);
        capture?.Started.TrySetResult(false);
        capture?.Stopped.TrySetResult(true);
        if (!_disposed)
        {
            SafeInvokeError(capture, $"Vosk 工作进程意外退出（代码 {TryGetExitCode(process)}）。");
        }

        RaiseDictationStateChanged();
        process.Dispose();
    }

    private void ClearCapture(CaptureSession expected)
    {
        WorkerSession? workerToStop = null;
        lock (_lock)
        {
            if (ReferenceEquals(_capture, expected))
            {
                _capture = null;
                if (_modelLeaseCount == 0 && _exclusiveOwner is null)
                {
                    workerToStop = _worker;
                }
            }
        }

        if (workerToStop is not null)
        {
            StopWorker(workerToStop);
        }
    }

    private void ReleaseModel(WorkerSession expected)
    {
        bool shouldStop;
        lock (_lock)
        {
            if (_modelLeaseCount > 0)
            {
                _modelLeaseCount--;
            }

            shouldStop = _modelLeaseCount == 0 &&
                         _capture is null &&
                         _exclusiveOwner is null &&
                         ReferenceEquals(_worker, expected);
        }

        if (shouldStop)
        {
            StopWorker(expected);
        }
    }

    private void ReleaseConversationReservation(object owner)
    {
        WorkerSession? workerToStop = null;
        var changed = false;
        lock (_lock)
        {
            if (ReferenceEquals(_exclusiveOwner, owner))
            {
                _exclusiveOwner = null;
                changed = true;
                if (_modelLeaseCount > 0)
                {
                    // Release the provisional lease acquired when the
                    // reservation was created, including failed startups.
                    _modelLeaseCount--;
                }
                if (_modelLeaseCount == 0 && _capture is null)
                {
                    workerToStop = _worker;
                }
            }
        }

        if (workerToStop is not null)
        {
            StopWorker(workerToStop);
        }

        if (changed)
        {
            RaiseDictationStateChanged();
        }
    }

    private void ReleaseConversation(WorkerSession expected, object owner)
    {
        WorkerSession? workerToStop = null;
        var changed = false;
        lock (_lock)
        {
            if (ReferenceEquals(_exclusiveOwner, owner))
            {
                _exclusiveOwner = null;
                changed = true;
            }

            if (_modelLeaseCount > 0)
            {
                _modelLeaseCount--;
            }

            if (_modelLeaseCount == 0 &&
                _capture is null &&
                ReferenceEquals(_worker, expected))
            {
                workerToStop = expected;
            }
        }

        if (workerToStop is not null)
        {
            StopWorker(workerToStop);
        }

        if (changed)
        {
            RaiseDictationStateChanged();
        }
    }

    private bool SendCommand(WorkerSession expected, string command)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(_worker, expected) || expected.Process.HasExited)
            {
                return false;
            }
        }

        lock (_stdinLock)
        {
            lock (_lock)
            {
                if (!ReferenceEquals(_worker, expected) || expected.Process.HasExited)
                {
                    return false;
                }

                try
                {
                    expected.Process.StandardInput.WriteLine(command);
                    expected.Process.StandardInput.Flush();
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[VoskSpeech] Failed to send worker command {Command}", command);
                    return false;
                }
            }
        }
    }

    private void StopWorker(WorkerSession? expected)
    {
        WorkerSession? worker;
        CaptureSession? capture;
        lock (_lock)
        {
            worker = _worker;
            if (worker is null || (expected is not null && !ReferenceEquals(worker, expected))) return;
            _worker = null;
            capture = _capture;
            _capture = null;
        }

        worker.Ready.TrySetResult(false);
        capture?.Started.TrySetResult(false);
        capture?.Stopped.TrySetResult(true);
        _ = Task.Run(() => ShutdownWorker(worker.Process));
        RaiseDictationStateChanged();
    }

    private static void ShutdownWorker(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.StandardInput.WriteLine("shutdown");
                process.StandardInput.Flush();
                if (!process.WaitForExit(WorkerExitTimeoutMilliseconds))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                }
            }
        }
        catch
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Process teardown is best effort.
            }
        }
        finally
        {
            process.Dispose();
        }
    }

    private static int TryGetExitCode(Process process)
    {
        try { return process.HasExited ? process.ExitCode : -1; }
        catch { return -1; }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        StopWorker(null);
    }

    private sealed class ModelLease(VoskSpeechService service, WorkerSession worker) : IDisposable
    {
        private VoskSpeechService? _service = service;
        public void Dispose() => Interlocked.Exchange(ref _service, null)?.ReleaseModel(worker);
    }

    private sealed class CaptureLease(VoskSpeechService service, CaptureSession capture) : IDisposable
    {
        private VoskSpeechService? _service = service;
        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _service, null);
            if (owner is not null)
            {
                _ = owner.StopCaptureAsync(capture, capture.Owner);
            }
        }
    }

    public sealed class ConversationSession : IAsyncDisposable
    {
        private VoskSpeechService? _service;
        private readonly WorkerSession _worker;
        private readonly object _owner;

        internal ConversationSession(
            VoskSpeechService service,
            WorkerSession worker,
            object owner)
        {
            _service = service;
            _worker = worker;
            _owner = owner;
        }

        public Task<IDisposable?> TryStartCaptureAsync(
            Action<string, bool> onText,
            Action<string> onError,
            Action onSpeechActivity,
            Action<double>? onAudioLevel = null,
            CancellationToken cancellationToken = default)
        {
            var service = Volatile.Read(ref _service);
            return service is null
                ? Task.FromResult<IDisposable?>(null)
                : service.TryStartCaptureCoreAsync(
                    onText,
                    onError,
                    onSpeechActivity,
                    onAudioLevel,
                    _owner,
                    cancellationToken);
        }

        public Task StopCaptureAsync()
        {
            var service = Volatile.Read(ref _service);
            return service is null
                ? Task.CompletedTask
                : service.StopCaptureAsync(expected: null, owner: _owner);
        }

        public async ValueTask DisposeAsync()
        {
            var service = Interlocked.Exchange(ref _service, null);
            if (service is null)
            {
                return;
            }

            await service.StopCaptureAsync(expected: null, owner: _owner);
            service.ReleaseConversation(_worker, _owner);
        }
    }

    private sealed class DictationLease(
        VoskSpeechService service,
        IDisposable capture,
        IDisposable model) : IDisposable
    {
        private VoskSpeechService? _service = service;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _service, null) is not null)
            {
                capture.Dispose();
                model.Dispose();
            }
        }
    }
}
