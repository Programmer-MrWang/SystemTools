using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using KaldiNativeFbankSharp;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Vosk;

namespace SystemTools.VoskWorker;

internal interface ISpeechRecognitionModel : IDisposable
{
    IRecognitionSession CreateSession();
}

internal interface IRecognitionSession : IDisposable
{
    WorkerMessage? AcceptAudio(byte[] audio);
    bool TryBeginPartialRecognition();
    Task<string?> GetPartialTextAsync();
    Task<string> GetFinalTextAsync();
}

internal static class SpeechRecognitionModelFactory
{
    private const string MarkerFileName = "copyright.txt";
    private const string MarkerPrefix = "Officially certified by SystemTools";

    public static ISpeechRecognitionModel Load(string modelDirectory)
    {
        var markerPath = Path.Combine(modelDirectory, MarkerFileName);
        if (!File.Exists(markerPath))
        {
            throw new InvalidDataException($"Model certificate is missing: {markerPath}");
        }

        var marker = File.ReadAllText(markerPath).Trim();
        if (!marker.StartsWith(MarkerPrefix, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The model certificate is invalid.");
        }

        var modelName = marker[MarkerPrefix.Length..].Trim().TrimStart('-', ':', '\uFF1A').Trim();
        if (modelName.Contains("SenseVoice", StringComparison.OrdinalIgnoreCase))
        {
            return new SenseVoiceRecognitionModel(modelDirectory);
        }

        if (modelName.Contains("Vosk", StringComparison.OrdinalIgnoreCase))
        {
            return new VoskRecognitionModel(modelDirectory);
        }

        if (LooksLikeSenseVoiceModel(modelDirectory))
        {
            return new SenseVoiceRecognitionModel(modelDirectory);
        }

        if (LooksLikeVoskModel(modelDirectory))
        {
            return new VoskRecognitionModel(modelDirectory);
        }

        throw new NotSupportedException(
            string.IsNullOrWhiteSpace(modelName)
                ? "Unable to determine the speech recognition model type."
                : $"Unsupported speech recognition model: {modelName}");
    }

    private static bool LooksLikeSenseVoiceModel(string path) =>
        File.Exists(Path.Combine(path, "model_quant.onnx")) &&
        File.Exists(Path.Combine(path, "am.mvn")) &&
        File.Exists(Path.Combine(path, "tokens.json"));

    private static bool LooksLikeVoskModel(string path) =>
        Directory.Exists(Path.Combine(path, "am")) &&
        Directory.Exists(Path.Combine(path, "conf")) &&
        Directory.Exists(Path.Combine(path, "graph"));
}

internal sealed class VoskRecognitionModel : ISpeechRecognitionModel
{
    private readonly Model _model;

    public VoskRecognitionModel(string modelDirectory)
    {
        Vosk.Vosk.SetLogLevel(-1);
        _model = new Model(modelDirectory);
    }

    public IRecognitionSession CreateSession() => new VoskRecognitionSession(_model);

    public void Dispose() => _model.Dispose();

    private sealed class VoskRecognitionSession(Model model) : IRecognitionSession
    {
        private readonly VoskRecognizer _recognizer = new(model, 16000);
        private string _lastPartial = string.Empty;
        private bool _disposed;

        public WorkerMessage? AcceptAudio(byte[] audio)
        {
            if (_disposed)
            {
                return null;
            }

            if (_recognizer.AcceptWaveform(audio, audio.Length))
            {
                var text = ReadText(_recognizer.Result(), "text");
                _lastPartial = string.Empty;
                return string.IsNullOrWhiteSpace(text)
                    ? null
                    : new WorkerMessage("final", text, null);
            }

            var partial = ReadText(_recognizer.PartialResult(), "partial");
            if (string.IsNullOrWhiteSpace(partial) ||
                string.Equals(partial, _lastPartial, StringComparison.Ordinal))
            {
                return null;
            }

            _lastPartial = partial;
            return new WorkerMessage("partial", partial, null);
        }

        public bool TryBeginPartialRecognition() => false;

        public Task<string?> GetPartialTextAsync() => Task.FromResult<string?>(null);

        public Task<string> GetFinalTextAsync() => Task.FromResult(
            _disposed ? string.Empty : ReadText(_recognizer.FinalResult(), "text"));

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _recognizer.Dispose();
        }

        private static string ReadText(string json, string propertyName)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(propertyName, out var value)
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }
    }
}

internal sealed class SenseVoiceRecognitionModel : ISpeechRecognitionModel
{
    private const int FeatureDimension = 560;
    private const int LanguageAuto = 0;
    private const int TextNormalizationWithItn = 14;
    private readonly InferenceSession _session;
    private readonly string[] _tokens;
    private readonly float[] _addShift;
    private readonly float[] _rescale;
    private readonly SemaphoreSlim _inferenceLock = new(1, 1);
    private bool _disposed;

    public SenseVoiceRecognitionModel(string modelDirectory)
    {
        var modelPath = RequireFile(modelDirectory, "model_quant.onnx");
        var cmvnPath = RequireFile(modelDirectory, "am.mvn");
        var tokensPath = RequireFile(modelDirectory, "tokens.json");

        (_addShift, _rescale) = ReadCmvn(cmvnPath);
        _tokens = JsonSerializer.Deserialize<string[]>(File.ReadAllText(tokensPath))
                  ?? throw new InvalidDataException("tokens.json is invalid.");

        using var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4),
            InterOpNumThreads = 1
        };
        _session = new InferenceSession(modelPath, options);
    }

    public IRecognitionSession CreateSession()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new SenseVoiceRecognitionSession(this);
    }

    internal async Task<string> RecognizeAsync(float[] features, int frameCount)
    {
        if (frameCount <= 0)
        {
            return string.Empty;
        }

        var lfrFeatures = ApplyLfrAndCmvn(features, frameCount);
        var lfrFrameCount = lfrFeatures.Length / FeatureDimension;
        if (lfrFrameCount == 0)
        {
            return string.Empty;
        }

        await _inferenceLock.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(
                    "speech",
                    new DenseTensor<float>(
                        lfrFeatures,
                        new[] { 1, lfrFrameCount, FeatureDimension })),
                NamedOnnxValue.CreateFromTensor(
                    "speech_lengths",
                    new DenseTensor<int>(new[] { lfrFrameCount }, new[] { 1 })),
                NamedOnnxValue.CreateFromTensor(
                    "language",
                    new DenseTensor<int>(new[] { LanguageAuto }, new[] { 1 })),
                NamedOnnxValue.CreateFromTensor(
                    "textnorm",
                    new DenseTensor<int>(new[] { TextNormalizationWithItn }, new[] { 1 }))
            };

            using var outputs = _session.Run(inputs);
            var logits = outputs.First(x => x.Name == "ctc_logits").AsTensor<float>();
            var outputLengths = outputs.First(x => x.Name == "encoder_out_lens").AsTensor<int>();
            return DecodeCtc(logits, outputLengths.FirstOrDefault());
        }
        finally
        {
            _inferenceLock.Release();
        }
    }

    private float[] ApplyLfrAndCmvn(float[] features, int frameCount)
    {
        const int melBins = 80;
        const int lfrWindow = 7;
        const int lfrStride = 6;
        const int leftPadding = 3;
        var outputFrames = (frameCount + lfrStride - 1) / lfrStride;
        var output = new float[outputFrames * FeatureDimension];

        for (var outputFrame = 0; outputFrame < outputFrames; outputFrame++)
        {
            for (var windowFrame = 0; windowFrame < lfrWindow; windowFrame++)
            {
                var sourceFrame = outputFrame * lfrStride + windowFrame - leftPadding;
                sourceFrame = Math.Clamp(sourceFrame, 0, frameCount - 1);
                var sourceOffset = sourceFrame * melBins;
                var destinationOffset = outputFrame * FeatureDimension + windowFrame * melBins;
                Array.Copy(features, sourceOffset, output, destinationOffset, melBins);
            }

            var frameOffset = outputFrame * FeatureDimension;
            for (var dimension = 0; dimension < FeatureDimension; dimension++)
            {
                output[frameOffset + dimension] =
                    (output[frameOffset + dimension] + _addShift[dimension]) * _rescale[dimension];
            }
        }

        return output;
    }

    private string DecodeCtc(Tensor<float> logits, int outputLength)
    {
        var dimensions = logits.Dimensions.ToArray();
        if (dimensions.Length != 3 || dimensions[0] != 1)
        {
            throw new InvalidDataException("Unexpected ctc_logits shape.");
        }

        var timeSteps = Math.Min(outputLength, dimensions[1]);
        var vocabularySize = dimensions[2];
        var data = logits.ToArray();
        var pieces = new List<string>();
        var previousToken = -1;

        for (var time = 0; time < timeSteps; time++)
        {
            var offset = time * vocabularySize;
            var bestToken = 0;
            var bestScore = data[offset];
            for (var token = 1; token < vocabularySize; token++)
            {
                var score = data[offset + token];
                if (score > bestScore)
                {
                    bestScore = score;
                    bestToken = token;
                }
            }

            if (bestToken != previousToken && bestToken != 0 && bestToken < _tokens.Length)
            {
                var piece = _tokens[bestToken];
                if (!IsControlToken(piece))
                {
                    pieces.Add(piece);
                }
            }

            previousToken = bestToken;
        }

        var text = string.Concat(pieces).Replace('\u2581', ' ').Trim();
        return Regex.Replace(text, @"\s+", " ");
    }

    private static bool IsControlToken(string token) =>
        token is "<unk>" or "<s>" or "</s>" ||
        (token.StartsWith("<|", StringComparison.Ordinal) &&
         token.EndsWith("|>", StringComparison.Ordinal));

    private static (float[] AddShift, float[] Rescale) ReadCmvn(string path)
    {
        var content = File.ReadAllText(path);
        var addShift = ReadCmvnVector(content, "AddShift");
        var rescale = ReadCmvnVector(content, "Rescale");
        if (addShift.Length != FeatureDimension || rescale.Length != FeatureDimension)
        {
            throw new InvalidDataException("am.mvn must contain 560-dimensional CMVN vectors.");
        }

        return (addShift, rescale);
    }

    private static float[] ReadCmvnVector(string content, string section)
    {
        var match = Regex.Match(
            content,
            $@"<{section}>.*?\[(?<values>.*?)\]",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            throw new InvalidDataException($"am.mvn does not contain {section}.");
        }

        return match.Groups["values"].Value
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => float.Parse(value, CultureInfo.InvariantCulture))
            .ToArray();
    }

    private static string RequireFile(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        return File.Exists(path)
            ? path
            : throw new FileNotFoundException($"SenseVoice model file is missing: {fileName}", path);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.Dispose();
        _inferenceLock.Dispose();
    }

    private sealed class SenseVoiceRecognitionSession(SenseVoiceRecognitionModel model) : IRecognitionSession
    {
        private const int SampleRate = 16000;
        private const int MinimumPartialSamples = SampleRate / 2;
        private const int PartialIntervalSamples = SampleRate;
        private readonly object _syncRoot = new();
        private readonly OnlineFbank _fbank = new(
            dither: 1,
            snip_edges: true,
            sample_rate: SampleRate,
            num_bins: 80,
            frame_shift: 10,
            frame_length: 25,
            energy_floor: 0,
            debug_mel: false,
            window_type: "hamming");
        private readonly List<float> _features = [];
        private int _sampleCount;
        private int _lastPartialSampleCount;
        private int _partialRecognitionRunning;
        private bool _inputFinished;
        private bool _disposed;

        public WorkerMessage? AcceptAudio(byte[] audio)
        {
            lock (_syncRoot)
            {
                if (_disposed || _inputFinished)
                {
                    return null;
                }

                var samples = new float[audio.Length / sizeof(short)];
                for (var i = 0; i < samples.Length; i++)
                {
                    samples[i] = BitConverter.ToInt16(audio, i * sizeof(short));
                }

                _sampleCount += samples.Length;
                _features.AddRange(_fbank.GetFbank(samples));
                return null;
            }
        }

        public bool TryBeginPartialRecognition()
        {
            lock (_syncRoot)
            {
                if (_disposed || _inputFinished ||
                    _sampleCount < MinimumPartialSamples ||
                    _sampleCount - _lastPartialSampleCount < PartialIntervalSamples ||
                    Interlocked.CompareExchange(ref _partialRecognitionRunning, 1, 0) != 0)
                {
                    return false;
                }

                _lastPartialSampleCount = _sampleCount;
                return true;
            }
        }

        public async Task<string?> GetPartialTextAsync()
        {
            try
            {
                var (features, frameCount) = SnapshotFeatures(final: false);
                return await model.RecognizeAsync(features, frameCount);
            }
            finally
            {
                Interlocked.Exchange(ref _partialRecognitionRunning, 0);
            }
        }

        public Task<string> GetFinalTextAsync()
        {
            var (features, frameCount) = SnapshotFeatures(final: true);
            return model.RecognizeAsync(features, frameCount);
        }

        private (float[] Features, int FrameCount) SnapshotFeatures(bool final)
        {
            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return ([], 0);
                }

                if (final && !_inputFinished)
                {
                    _inputFinished = true;
                    _fbank.InputFinished();
                    _features.AddRange(_fbank.GetFbank([]));
                }

                return (_features.ToArray(), _features.Count / 80);
            }
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _fbank.Dispose();
                _features.Clear();
            }
        }
    }
}

internal sealed record WorkerMessage(
    string Type,
    string? Text = null,
    string? Message = null,
    double? Level = null);
