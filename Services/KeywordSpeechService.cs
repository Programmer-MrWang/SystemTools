using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Speech.Recognition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SystemTools.Services;

public class KeywordSpeechService : IDisposable
{
    private const int MaximumDictationContextLength = 120;
    private const int MaximumRecognitionAlternates = 8;

    private readonly ILogger<KeywordSpeechService> _logger;
    private SpeechRecognitionEngine? _engine;
    private Thread? _thread;
    private volatile bool _disposed;
    private readonly object _lock = new();
    private readonly List<RegisteredKeyword> _registrations = new();
    private RegisteredDictation? _dictation;
    private int _listeningSuspensionCount;

    private class RegisteredKeyword
    {
        public string Keyword { get; init; } = "";
        public double Threshold { get; init; }
        public Action? OnMatched { get; init; }
        public Action<IDisposable>? OnWakeMatched { get; init; }
        public bool IsWakeWord { get; init; }
    }

    private sealed class RegisteredDictation
    {
        public required Action<string, bool> OnText { get; init; }
        public required Action<string> OnError { get; init; }
        public string Context { get; init; } = string.Empty;
        public string LastHypothesis { get; set; } = string.Empty;
    }

    private readonly record struct RecognitionCandidate(
        string Text,
        string NormalizedText,
        double Confidence);

    public bool IsListening => _engine != null;

    public bool IsDictationActive
    {
        get
        {
            lock (_lock)
            {
                return _dictation != null;
            }
        }
    }

    public event EventHandler? DictationStateChanged;

    public KeywordSpeechService(ILogger<KeywordSpeechService> logger)
    {
        _logger = logger;
    }

    public IDisposable Register(string keyword, double threshold, Action onMatched)
        => RegisterCore(keyword, threshold, onMatched, onWakeMatched: null, isWakeWord: false);

    public IDisposable RegisterWakeWord(
        string keyword,
        double threshold,
        Action<IDisposable> onMatched)
        => RegisterCore(keyword, threshold, onMatched: null, onWakeMatched: onMatched, isWakeWord: true);

    private IDisposable RegisterCore(
        string keyword,
        double threshold,
        Action? onMatched,
        Action<IDisposable>? onWakeMatched,
        bool isWakeWord)
    {
        var reg = new RegisteredKeyword
        {
            Keyword = NormalizeForComparison(keyword),
            Threshold = Math.Clamp(threshold, 0.0, 1.0),
            OnMatched = onMatched,
            OnWakeMatched = onWakeMatched,
            IsWakeWord = isWakeWord
        };
        lock (_lock) { _registrations.Add(reg); }
        EnsureStarted();
        _logger.LogDebug("[KeywordSpeech] Registered {Kind}: \"{Keyword}\" (threshold: {Threshold:F2})",
            isWakeWord ? "wake word" : "keyword", keyword, threshold);
        return new UnregisterHandle(this, reg);
    }

    public IDisposable SuspendListening()
        => AcquireListeningSuspension(stopEngineSynchronously: true);

    /// <summary>
    /// Acquires the logical suppression used by a wake-word callback before
    /// dispatching that callback. The SAPI engine is stopped asynchronously so
    /// the recognition thread can return promptly, but subsequent results are
    /// ignored immediately under the same lock that checks the registrations.
    /// </summary>
    private IDisposable AcquireWakeWordSuspension()
        => AcquireListeningSuspension(stopEngineSynchronously: false);

    private IDisposable AcquireListeningSuspension(bool stopEngineSynchronously)
    {
        bool shouldStop;
        lock (_lock)
        {
            shouldStop = _listeningSuspensionCount++ == 0;
        }

        if (shouldStop && stopEngineSynchronously)
        {
            StopEngine();
        }
        else if (shouldStop)
        {
            _ = Task.Run(StopEngineIfSuspended);
        }

        return new ListeningSuspensionHandle(this);
    }

    public IDisposable? TryStartDictation(
        Action<string, bool> onText,
        Action<string> onError,
        string? context = null)
    {
        ArgumentNullException.ThrowIfNull(onText);
        ArgumentNullException.ThrowIfNull(onError);

        if (!OperatingSystem.IsWindows())
        {
            onError("语音输入仅支持 Windows。");
            return null;
        }

        RegisteredDictation dictation;
        lock (_lock)
        {
            if (_disposed || _dictation != null)
            {
                return null;
            }

            dictation = new RegisteredDictation
            {
                OnText = onText,
                OnError = onError,
                Context = BuildDictationContext(context)
            };
            _dictation = dictation;
        }

        // Recreate the dictation grammar so the new conversation context is applied.
        if (_engine != null)
        {
            StopEngine();
        }
        EnsureStarted();
        DictationStateChanged?.Invoke(this, EventArgs.Empty);
        _logger.LogInformation("[KeywordSpeech] AI dictation started");
        return new DictationHandle(this, dictation);
    }

    private void Unregister(RegisteredKeyword reg)
    {
        bool shouldStop;
        lock (_lock)
        {
            _registrations.Remove(reg);
            shouldStop = _registrations.Count == 0 && _dictation == null;
        }
        _logger.LogDebug("[KeywordSpeech] Unregistered: \"{Keyword}\"", reg.Keyword);
        if (shouldStop)
        {
            StopEngine();
        }
    }

    private void StopDictation(RegisteredDictation dictation)
    {
        bool shouldStop;
        lock (_lock)
        {
            if (!ReferenceEquals(_dictation, dictation))
            {
                return;
            }

            _dictation = null;
            shouldStop = _registrations.Count == 0;
        }

        if (shouldStop)
        {
            StopEngine();
        }

        DictationStateChanged?.Invoke(this, EventArgs.Empty);
        _logger.LogInformation("[KeywordSpeech] AI dictation stopped");
    }

    public void EnsureStarted()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (_engine != null) return;
        lock (_lock)
        {
            if (_engine != null || _listeningSuspensionCount > 0) return;
            if (_thread is { IsAlive: true }) return;
            _thread = new Thread(SpeechThread)
            {
                IsBackground = true,
                Name = "KeywordSpeech"
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }
    }

    private void SpeechThread()
    {
        var startFailed = false;
        SpeechRecognitionEngine? createdEngine = null;
        try
        {
            var culture = new CultureInfo("zh-CN");
            var engine = new SpeechRecognitionEngine(culture);
            createdEngine = engine;
            engine.SetInputToDefaultAudioDevice();
            TryConfigureDictationEngine(engine);

            var dictationGrammar = new DictationGrammar();
            RegisteredDictation? activeDictation;
            lock (_lock)
            {
                activeDictation = _dictation;
            }
            if (!string.IsNullOrWhiteSpace(activeDictation?.Context))
            {
                try
                {
                    dictationGrammar.SetDictationContext(activeDictation.Context, null);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(
                        ex,
                        "[KeywordSpeech] Current SAPI engine does not support dictation context");
                }
            }
            engine.LoadGrammar(dictationGrammar);
            engine.SpeechHypothesized += OnSpeechHypothesized;
            engine.SpeechRecognized += OnSpeechRecognized;

            lock (_lock)
            {
                if (_disposed ||
                    _listeningSuspensionCount > 0 ||
                    (_registrations.Count == 0 && _dictation == null))
                {
                    engine.Dispose();
                    return;
                }

                _engine = engine;
            }

            engine.RecognizeAsync(RecognizeMode.Multiple);
            _logger.LogInformation("[KeywordSpeech] Started (zh-CN)");
            while (!_disposed)
            {
                lock (_lock)
                {
                    if (!ReferenceEquals(_engine, engine))
                    {
                        break;
                    }
                }

                Thread.Sleep(500);
            }
        }
        catch (Exception ex)
        {
            startFailed = true;
            _logger.LogError(ex, "[KeywordSpeech] Start failed: {Message}", ex.Message);
            lock (_lock)
            {
                if (ReferenceEquals(_engine, createdEngine))
                {
                    _engine = null;
                }
            }
            DisposeEngine(createdEngine);
            FailDictation($"无法启动语音输入：{ex.Message}");
        }
        finally
        {
            bool shouldRestart;
            lock (_lock)
            {
                _thread = null;
                shouldRestart = !startFailed &&
                                !_disposed &&
                                _listeningSuspensionCount == 0 &&
                                _engine == null &&
                                (_registrations.Count > 0 || _dictation != null);
            }

            if (shouldRestart)
            {
                EnsureStarted();
            }
        }
    }

    private void OnSpeechHypothesized(object? sender, SpeechHypothesizedEventArgs e)
    {
        RegisteredDictation? dictation;
        lock (_lock)
        {
            if (_listeningSuspensionCount > 0)
            {
                return;
            }
            dictation = _dictation;
        }

        if (dictation == null || string.IsNullOrWhiteSpace(e.Result.Text))
        {
            return;
        }

        dictation.LastHypothesis = e.Result.Text;
        dictation.OnText(e.Result.Text, false);
    }

    private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        if (_disposed) return;
        string text;
        double confidence;
        try
        {
            text = e.Result.Text;
            confidence = e.Result.Confidence;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[KeywordSpeech] SAPI internal error ignored");
            return;
        }

        RegisteredDictation? dictation;
        lock (_lock)
        {
            if (_listeningSuspensionCount > 0)
            {
                return;
            }
            dictation = _dictation;
        }
        if (dictation != null)
        {
            var selected = SelectBestDictationCandidate(e.Result, dictation);
            _logger.LogDebug(
                "[KeywordSpeech] Dictation committed (text: \"{Text}\", confidence: {Confidence:F2})",
                selected.Text,
                selected.Confidence);
            dictation.OnText(selected.Text, true);
            dictation.LastHypothesis = string.Empty;
        }

        RegisteredKeyword[] snapshot;
        lock (_lock) { snapshot = _registrations.ToArray(); }
        if (snapshot.Length == 0) return;

        var candidates = GetRecognitionCandidates(e.Result, text, confidence);
        var wakeMatches = new List<(RegisteredKeyword Registration, RecognitionCandidate Candidate)>();
        foreach (var reg in snapshot)
        {
            if (!reg.IsWakeWord)
            {
                continue;
            }

            foreach (var candidate in candidates)
            {
                if (!IsMatch(reg, candidate.NormalizedText, candidate.Confidence))
                {
                    continue;
                }

                wakeMatches.Add((reg, candidate));
                break;
            }
        }

        foreach (var (reg, candidate) in wakeMatches)
        {
            var suspension = AcquireWakeWordSuspension();
            try
            {
                _logger.LogInformation(
                    "[KeywordSpeech] Matched: \"{Keyword}\" (text: \"{Text}\", confidence: {Confidence:F2})",
                    reg.Keyword,
                    candidate.Text,
                    candidate.Confidence);
                if (reg.OnWakeMatched is null)
                {
                    suspension.Dispose();
                }
                else
                {
                    reg.OnWakeMatched(suspension);
                }
            }
            catch (Exception ex)
            {
                suspension.Dispose();
                _logger.LogError(ex, "[KeywordSpeech] Wake word callback failed");
            }
        }

        // A wake phrase owns the recognition result. This prevents the same phrase
        // from firing the ordinary automation keyword triggers in the same pass.
        if (wakeMatches.Count > 0)
        {
            return;
        }

        var normalized = NormalizeForComparison(text);
        foreach (var reg in snapshot)
        {
            if (reg.IsWakeWord || !IsMatch(reg, normalized, confidence)) continue;
            try
            {
                _logger.LogInformation("[KeywordSpeech] Matched: \"{Keyword}\" (text: \"{Text}\", confidence: {Confidence:F2})", reg.Keyword, text, confidence);
                reg.OnMatched?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[KeywordSpeech] Keyword callback failed");
            }
        }
    }

    private static bool IsMatch(RegisteredKeyword registration, string normalized, double confidence)
    {
        return registration.Keyword.Length > 0 &&
               confidence >= registration.Threshold &&
               normalized.Contains(registration.Keyword, StringComparison.OrdinalIgnoreCase);
    }

    private List<RecognitionCandidate> GetRecognitionCandidates(
        RecognitionResult result,
        string primaryText,
        double primaryConfidence)
    {
        var candidates = new List<RecognitionCandidate>
        {
            new(primaryText, NormalizeForComparison(primaryText), primaryConfidence)
        };

        try
        {
            foreach (var alternate in result.Alternates)
            {
                if (candidates.Count >= MaximumRecognitionAlternates + 1)
                {
                    break;
                }

                var alternateText = alternate.Text;
                if (string.IsNullOrWhiteSpace(alternateText))
                {
                    continue;
                }

                candidates.Add(new(
                    alternateText,
                    NormalizeForComparison(alternateText),
                    alternate.Confidence));
            }
        }
        catch (Exception ex)
        {
            // A malformed alternate must not hide the primary recognition result.
            _logger.LogDebug(ex, "[KeywordSpeech] SAPI alternates unavailable; using primary result");
        }

        return candidates;
    }

    private void StopEngine()
    {
        SpeechRecognitionEngine? engine;
        lock (_lock)
        {
            engine = _engine;
            _engine = null;
        }

        DisposeEngine(engine);
    }

    private void StopEngineIfSuspended()
    {
        SpeechRecognitionEngine? engine;
        lock (_lock)
        {
            if (_listeningSuspensionCount == 0)
            {
                return;
            }

            // Detach the engine while holding the same lock used by
            // ResumeListening. If the suspension is released immediately,
            // a newly created engine cannot be stopped by this cleanup.
            engine = _engine;
            _engine = null;
        }

        DisposeEngine(engine);
    }

    private void DisposeEngine(SpeechRecognitionEngine? engine)
    {
        try
        {
            if (engine != null)
            {
                engine.SpeechHypothesized -= OnSpeechHypothesized;
                engine.SpeechRecognized -= OnSpeechRecognized;
                engine.RecognizeAsyncStop();
                engine.Dispose();
                _logger.LogInformation("[KeywordSpeech] Stopped");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[KeywordSpeech] Stop error: {Message}", ex.Message);
        }
    }

    private void ResumeListening()
    {
        bool shouldStart;
        lock (_lock)
        {
            if (_listeningSuspensionCount == 0)
            {
                return;
            }

            _listeningSuspensionCount--;
            shouldStart = _listeningSuspensionCount == 0 &&
                          !_disposed &&
                          (_registrations.Count > 0 || _dictation != null);
        }

        if (shouldStart)
        {
            EnsureStarted();
        }
    }

    private void FailDictation(string message)
    {
        RegisteredDictation? dictation;
        lock (_lock)
        {
            dictation = _dictation;
            _dictation = null;
        }

        if (dictation == null)
        {
            return;
        }

        dictation.OnError(message);
        DictationStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void TryConfigureDictationEngine(SpeechRecognitionEngine engine)
    {
        try
        {
            engine.MaxAlternates = MaximumRecognitionAlternates;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[KeywordSpeech] Current SAPI engine does not support alternate count");
        }

        try
        {
            engine.EndSilenceTimeout = TimeSpan.FromMilliseconds(900);
            engine.EndSilenceTimeoutAmbiguous = TimeSpan.FromMilliseconds(1300);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[KeywordSpeech] Current SAPI engine does not support silence tuning");
        }
    }

    private static string BuildDictationContext(string? context)
    {
        var userContext = string.IsNullOrWhiteSpace(context)
            ? string.Empty
            : context.Trim();
        if (userContext.Length > MaximumDictationContextLength)
        {
            userContext = userContext[^MaximumDictationContextLength..];
        }

        return userContext;
    }

    private static (string Text, double Confidence) SelectBestDictationCandidate(
        RecognitionResult result,
        RegisteredDictation dictation)
    {
        var primaryText = string.IsNullOrWhiteSpace(result.Text)
            ? dictation.LastHypothesis
            : result.Text;
        var selectedText = primaryText;
        var selectedConfidence = (double)result.Confidence;
        var selectedScore = ScoreDictationCandidate(
            primaryText,
            selectedConfidence,
            dictation.LastHypothesis,
            isPrimary: true);

        try
        {
            foreach (var alternate in result.Alternates)
            {
                if (string.IsNullOrWhiteSpace(alternate.Text))
                {
                    continue;
                }

                var alternateScore = ScoreDictationCandidate(
                    alternate.Text,
                    alternate.Confidence,
                    dictation.LastHypothesis,
                    isPrimary: false);
                if (alternateScore > selectedScore + 0.06)
                {
                    selectedText = alternate.Text;
                    selectedConfidence = alternate.Confidence;
                    selectedScore = alternateScore;
                }
            }
        }
        catch
        {
            // Some SAPI engines do not expose alternates consistently.
        }

        return (selectedText, selectedConfidence);
    }

    private static double ScoreDictationCandidate(
        string text,
        double confidence,
        string lastHypothesis,
        bool isPrimary)
    {
        var hypothesisSimilarity = CalculateTextSimilarity(text, lastHypothesis);
        return confidence * 0.85 +
               hypothesisSimilarity * 0.15 +
               (isPrimary ? 0.05 : 0.0);
    }

    private static double CalculateTextSimilarity(string text, string reference)
    {
        var normalizedText = NormalizeForComparison(text);
        var normalizedReference = NormalizeForComparison(reference);
        if (normalizedText.Length == 0 || normalizedReference.Length == 0)
        {
            return 0;
        }

        if (normalizedReference.Contains(normalizedText, StringComparison.OrdinalIgnoreCase) ||
            normalizedText.Contains(normalizedReference, StringComparison.OrdinalIgnoreCase))
        {
            return Math.Min(normalizedText.Length, normalizedReference.Length) /
                   (double)Math.Max(normalizedText.Length, normalizedReference.Length);
        }

        var textCharacters = normalizedText.ToHashSet();
        var referenceCharacters = normalizedReference.ToHashSet();
        var intersection = textCharacters.Count(character => referenceCharacters.Contains(character));
        return 2.0 * intersection / (textCharacters.Count + referenceCharacters.Count);
    }

    private static string NormalizeForComparison(string text)
    {
        return new string(text
            .Where(character => !char.IsWhiteSpace(character) && !char.IsPunctuation(character))
            .ToArray());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_lock)
        {
            _dictation = null;
        }
        StopEngine();
        DictationStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private class UnregisterHandle : IDisposable
    {
        private readonly KeywordSpeechService _service;
        private readonly RegisteredKeyword _reg;
        public UnregisterHandle(KeywordSpeechService service, RegisteredKeyword reg)
        {
            _service = service;
            _reg = reg;
        }
        public void Dispose() { _service.Unregister(_reg); }
    }

    private sealed class DictationHandle : IDisposable
    {
        private KeywordSpeechService? _service;
        private readonly RegisteredDictation _dictation;

        public DictationHandle(KeywordSpeechService service, RegisteredDictation dictation)
        {
            _service = service;
            _dictation = dictation;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _service, null)?.StopDictation(_dictation);
        }
    }

    private sealed class ListeningSuspensionHandle(KeywordSpeechService service) : IDisposable
    {
        private KeywordSpeechService? _service = service;

        public void Dispose()
        {
            Interlocked.Exchange(ref _service, null)?.ResumeListening();
        }
    }
}
