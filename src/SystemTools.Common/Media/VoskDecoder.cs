using System.Runtime.InteropServices;
using System.Text.Json;
using SystemTools.Contracts;

namespace SystemTools.Common.Media;

public sealed class VoskDecoder : IDisposable
{
    private nint _library;
    private nint _model;
    private nint _recognizer;
    private readonly Free _freeModel;
    private readonly Free _freeRecognizer;
    private readonly Accept _accept;
    private readonly Result _result;
    private readonly Result _partial;
    private readonly Result _final;
    private int _disposed;

    public VoskDecoder(string libraryPath, string modelPath)
    {
        if (!MediaFiles.IsVoskModel(modelPath)) throw new NativeFeatureException(CapabilityState.MissingDependency, "请选择完整的 Vosk 离线模型文件夹。");
        _library = NativeLibrary.Load(Path.GetFullPath(libraryPath));
        try
        {
            T Function<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));
            _freeModel = Function<Free>("vosk_model_free");
            _freeRecognizer = Function<Free>("vosk_recognizer_free");
            _accept = Function<Accept>("vosk_recognizer_accept_waveform");
            _result = Function<Result>("vosk_recognizer_result");
            _partial = Function<Result>("vosk_recognizer_partial_result");
            _final = Function<Result>("vosk_recognizer_final_result");
            Function<SetInteger>("vosk_set_log_level")(-1);
            _model = Function<ModelNew>("vosk_model_new")(modelPath);
            if (_model == 0) throw new IOException("语音模型无法加载，请检查文件完整性。");
            _recognizer = Function<RecognizerNew>("vosk_recognizer_new")(_model, 16000);
            if (_recognizer == 0) throw new IOException("无法创建语音识别会话。");
            Function<SetWords>("vosk_recognizer_set_words")(_recognizer, 1);
        }
        catch
        {
            if (_recognizer != 0) _freeRecognizer?.Invoke(_recognizer);
            if (_model != 0) _freeModel?.Invoke(_model);
            NativeLibrary.Free(_library);
            _library = 0;
            throw;
        }
    }

    public SpeechSegment? AcceptPcm16(byte[] samples)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (samples.Length % 2 != 0) throw new ArgumentException("PCM16 数据必须按完整采样对齐。", nameof(samples));
        var accepted = _accept(_recognizer, samples, samples.Length);
        if (accepted < 0) throw new IOException("语音数据未能解码。");
        return Parse(Marshal.PtrToStringUTF8((accepted == 1 ? _result : _partial)(_recognizer))!, accepted == 1);
    }

    public SpeechSegment? Finish()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        return Parse(Marshal.PtrToStringUTF8(_final(_recognizer))!, true);
    }

    public static SpeechSegment? Parse(string json, bool final)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var text = root.TryGetProperty(final ? "text" : "partial", out var value) ? value.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(text)) return null;
        double? confidence = null;
        if (root.TryGetProperty("result", out var words))
        {
            var scores = words.EnumerateArray().Where(word => word.TryGetProperty("conf", out _))
                .Select(word => word.GetProperty("conf").GetDouble()).ToArray();
            if (scores.Length > 0) confidence = scores.Average();
        }
        return new(text, final, confidence);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_recognizer != 0) _freeRecognizer(_recognizer);
        if (_model != 0) _freeModel(_model);
        if (_library != 0) NativeLibrary.Free(_library);
        _recognizer = _model = _library = 0;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint ModelNew([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint RecognizerNew(nint model, float rate);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Free(nint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Accept(nint recognizer, [In] byte[] data, int length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint Result(nint recognizer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetWords(nint recognizer, int enabled);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetInteger(int value);
}
