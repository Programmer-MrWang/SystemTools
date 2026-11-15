using SystemTools.Common.Media;
using SystemTools.Contracts;

var checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine($"PASS {++checks}: {name}"); }
var devices = new DeviceReservation();
var lease = devices.Acquire("microphone", default);
try { devices.Acquire("microphone", default); throw new Exception("Busy device reopened"); }
catch (NativeFeatureException) { Check(true, "同一麦克风拒绝并发占用"); }
using (devices.Acquire("camera", default)) Check(true, "相机与麦克风所有权相互独立");
lease.Dispose(); lease.Dispose();
using (devices.Acquire("microphone", default)) Check(true, "重复释放幂等且设备可重新获取");
using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
try { devices.Acquire("microphone", cancelled.Token); throw new Exception("Cancelled owner acquired"); }
catch (OperationCanceledException) { Check(true, "预取消不会占有设备"); }
using (devices.Acquire("microphone", default)) Check(true, "取消未泄漏设备所有权");
var disposed = new ProbeResource();
using (var media = new PlatformMediaServices { Camera = disposed, Speech = disposed, Authentication = disposed, Media = disposed })
{
    media.Stopping.Register(() => throw new IOException("synthetic stop callback failure"));
    try { media.Dispose(); } catch (AggregateException) { }
    Check(disposed.Disposals == 1, "停止回调失败仍释放各项媒体服务且同一实例只释放一次");
    media.Dispose();
}
Check(disposed.Disposals == 1, "重复媒体停止幂等");
var temporary = Path.Combine(Path.GetTempPath(), "systemtools-m5-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
try
{
    var camera = new ProbeResource();
    try { await MediaFiles.CaptureAsync(camera, "test", temporary, default); throw new Exception("Broken image accepted"); }
    catch (EndOfStreamException) { Check(!Directory.EnumerateFiles(temporary).Any(), "坏图片不提交且临时文件被清理"); }
    Check(MediaFiles.FindExecutable("missing", Path.Combine(temporary, "missing")) is null, "显式错误依赖路径不偷偷回退到其他可执行文件");
}
finally { Directory.Delete(temporary, recursive: true); }
await MediaConcurrencyTests.RunAsync(Check);
Console.WriteLine($"M5 checks: {checks}");

if (args.Contains("--camera"))
{
    var camera = new FfmpegCamera("win-x64", () => MediaFiles.FindExecutable("ffmpeg"));
    var available = await camera.ListDevicesAsync(default);
    Check(available.Any(device => device.Id == "ASUS FHD webcam"), "真实 DirectShow 发现指定 ASUS 摄像头");
    var folder = Path.GetFullPath("artifacts/m5/camera");
    var images = new List<string>();
    for (var i = 0; i < 2; i++)
    {
        var image = await MediaFiles.CaptureAsync(camera, "ASUS FHD webcam", folder, default);
        MediaFiles.RequirePng(image);
        images.Add(image);
    }
    Check(images.Distinct().Count() == 2, "真实抓拍重复进入且输出文件名不冲突");
    var before = Directory.GetFiles(folder).Length;
    try { await MediaFiles.CaptureAsync(camera, "SystemTools-Missing-Device", folder, default); throw new Exception("Unknown device captured"); }
    catch (NativeFeatureException) { Check(Directory.GetFiles(folder).Length == before, "不存在设备明确失败且无半成品"); }
    using var stop = new CancellationTokenSource(); stop.Cancel();
    try { await MediaFiles.CaptureAsync(camera, "ASUS FHD webcam", folder, stop.Token); throw new Exception("Cancelled camera captured"); }
    catch (OperationCanceledException) { Check(Directory.GetFiles(folder).Length == before, "预取消不进入真实摄像头"); }
    File.WriteAllText("artifacts/m5/camera.json", System.Text.Json.JsonSerializer.Serialize(new { passed = true, devices = available, images, checks }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
}

if (args.Contains("--vosk"))
{
    string Option(string key) => args[Array.IndexOf(args, key) + 1];
    using var decoder = new VoskDecoder(Option("--library"), Option("--model"));
    var wave = File.ReadAllBytes(Option("--wave"));
    var cursor = 12;
    while (!wave.AsSpan(cursor, 4).SequenceEqual("data"u8))
        cursor += 8 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(cursor + 4, 4));
    var bytes = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(cursor + 4, 4));
    var segments = new List<string>();
    for (var position = cursor + 8; position < cursor + 8 + bytes; position += 3200)
    {
        var length = Math.Min(3200, cursor + 8 + bytes - position);
        if (decoder.AcceptPcm16(wave.AsSpan(position, length).ToArray()) is { IsFinal: true } text) segments.Add(text.Text);
    }
    if (decoder.Finish() is { } last) segments.Add(last.Text);
    var transcript = string.Join(" ", segments);
    Check(transcript.Contains("zero", StringComparison.Ordinal) && transcript.Contains("one", StringComparison.Ordinal), "真实 Vosk 模型识别官方语音夹具的已知内容");
    File.WriteAllText("artifacts/m5/vosk-file.json", System.Text.Json.JsonSerializer.Serialize(new { passed = true, transcript, segments }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
}

sealed class ProbeResource : ICameraCapture, ISpeechRecognition, ISystemAuthentication, IMediaSessions, IDisposable
{
    public int Disposals;
    public Capability Probe() => new(CapabilityState.Available);
    public Task<IReadOnlyList<CaptureDevice>> ListDevicesAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<CaptureDevice>>([]);
    public Task CapturePngAsync(string id, string destination, CancellationToken token) { File.WriteAllText(destination, "broken"); return Task.CompletedTask; }
    public async IAsyncEnumerable<SpeechSegment> RecognizeAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token) { await Task.CompletedTask; yield break; }
    public Task<AuthenticationResult> VerifyAsync(string reason, nint handle, CancellationToken token) => Task.FromResult(new AuthenticationResult(false, "synthetic"));
    public Task<IReadOnlyList<MediaSession>> ReadAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<MediaSession>>([]);
    public void Dispose() => Disposals++;
}
