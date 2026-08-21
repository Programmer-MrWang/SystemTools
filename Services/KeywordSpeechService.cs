using System;
using System.Collections.Generic;
using System.Globalization;
using System.Speech.Recognition;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace SystemTools.Services;

public class KeywordSpeechService : IDisposable
{
    private readonly ILogger<KeywordSpeechService> _logger;
    private SpeechRecognitionEngine? _engine;
    private Thread? _thread;
    private volatile bool _disposed;
    private readonly object _lock = new();
    private readonly List<RegisteredKeyword> _registrations = new();

    private class RegisteredKeyword
    {
        public string Keyword { get; init; } = "";
        public double Threshold { get; init; }
        public Action? OnMatched { get; init; }
    }

    public bool IsListening => _engine != null;

    public KeywordSpeechService(ILogger<KeywordSpeechService> logger)
    {
        _logger = logger;
    }

    public IDisposable Register(string keyword, double threshold, Action onMatched)
    {
        var reg = new RegisteredKeyword
        {
            Keyword = keyword,
            Threshold = Math.Clamp(threshold, 0.0, 1.0),
            OnMatched = onMatched
        };
        lock (_lock) { _registrations.Add(reg); }
        EnsureStarted();
        _logger.LogDebug("[KeywordSpeech] Registered: \"{Keyword}\" (threshold: {Threshold:F2})", keyword, threshold);
        return new UnregisterHandle(this, reg);
    }

    private void Unregister(RegisteredKeyword reg)
    {
        lock (_lock) { _registrations.Remove(reg); }
        _logger.LogDebug("[KeywordSpeech] Unregistered: \"{Keyword}\"", reg.Keyword);
        lock (_lock) { if (_registrations.Count == 0) StopEngine(); }
    }

    public void EnsureStarted()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (_engine != null) return;
        lock (_lock)
        {
            if (_engine != null) return;
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
        try
        {
            var culture = new CultureInfo("zh-CN");
            _engine = new SpeechRecognitionEngine(culture);
            _engine.SetInputToDefaultAudioDevice();
            _engine.LoadGrammar(new DictationGrammar());
            _engine.SpeechRecognized += OnSpeechRecognized;
            _engine.RecognizeAsync(RecognizeMode.Multiple);
            _logger.LogInformation("[KeywordSpeech] Started (zh-CN)");
            while (!_disposed && _engine != null) { Thread.Sleep(500); }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[KeywordSpeech] Start failed: {Message}", ex.Message);
            _engine?.Dispose();
            _engine = null;
        }
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

        RegisteredKeyword[] snapshot;
        lock (_lock) { snapshot = _registrations.ToArray(); }
        if (snapshot.Length == 0) return;

        var normalized = text.Replace(" ", "");
        foreach (var reg in snapshot)
        {
            if (string.IsNullOrWhiteSpace(reg.Keyword)) continue;
            if (confidence < reg.Threshold) continue;
            if (normalized.Contains(reg.Keyword, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("[KeywordSpeech] Matched: \"{Keyword}\" (text: \"{Text}\", confidence: {Confidence:F2})", reg.Keyword, text, confidence);
                reg.OnMatched?.Invoke();
            }
        }
    }

    private void StopEngine()
    {
        try
        {
            if (_engine != null)
            {
                _engine.SpeechRecognized -= OnSpeechRecognized;
                _engine.RecognizeAsyncStop();
                _engine.Dispose();
                _engine = null;
                _logger.LogInformation("[KeywordSpeech] Stopped");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[KeywordSpeech] Stop error: {Message}", ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopEngine();
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
}
