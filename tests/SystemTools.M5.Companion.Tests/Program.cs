using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using SystemTools.Common.Media;
using SystemTools.Contracts;
using SystemTools.Platforms.MacOS;

if (OperatingSystem.IsWindows()) { Console.WriteLine("SKIP: Unix local IPC tests require a Unix OS."); return; }
var checks = new List<string>();
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks.Add(message); Console.WriteLine($"PASS {checks.Count}: {message}");
}
var output = Path.GetFullPath("artifacts/m5/mac-companion");
Directory.CreateDirectory(output);
if (args.Contains("--native"))
{
    if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
    var app = Path.GetFullPath("src/SystemTools.Platforms.MacOS/bin/Release/net10.0/SystemTools Companion.app");
    using var client = new CompanionClient(app, async (application, session, token) =>
    {
        var start = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
        foreach (var argument in new[] { "-n", "-g", "-a", application, "--args", "--session", session }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        await process.WaitForExitAsync(token);
        if (process.ExitCode != 0) throw new IOException("LaunchServices failed.");
    });
    var status = await client.RequireAsync("session.status", null, default);
    Check(status.GetProperty("bundleId").GetString() == CompanionClient.BundleIdentifier, "真实独立应用身份返回插件");
    Check(status.GetProperty("parentPid").GetInt32() == 1, "采集助手通过 LaunchServices 启动并由 launchd 托管");
    Check(NativeBridge.Status(NativeBridge.Call(new { operation = "camera.request" })).State == CapabilityState.Unsupported,
        "宿主内相机入口拒绝授权和采集");
    Check(NativeBridge.Status(NativeBridge.Call(new { operation = "microphone.request" })).State == CapabilityState.Unsupported,
        "宿主内麦克风入口拒绝授权和采集");
    var capabilities = new Dictionary<string, Capability>();
    foreach (var device in new[] { "camera", "microphone" })
    {
        capabilities[device] = await client.ProbeAsync(device, default);
        Check(capabilities[device].State != CapabilityState.MissingDependency, device + " 独立用途声明就绪且只读预检完成");
        var id = Guid.NewGuid().ToString("D");
        await client.RequireAsync(device + ".prepare", new { id }, default);
        await client.RequireAsync(device == "camera" ? "camera.cancel" : "microphone.stop", new { id }, default);
        await client.EndSessionAsync(device + ".release", id);
    }
    status = await client.RequireAsync("session.status", null, default);
    Check(status.GetProperty("cameraJobs").GetInt32() == 0 && status.GetProperty("microphoneJobs").GetInt32() == 0,
        "真实辅助应用准备、取消、释放后不残留设备会话");
    var invalid = await client.CallAsync("camera.capture", new { id = "../invalid", device = "unknown" }, default);
    Check(NativeBridge.Status(invalid).State == CapabilityState.Unsupported, "辅助应用拒绝无效会话及路径穿越");
    if (args.Contains("--devices"))
    {
        foreach (var device in new[] { "camera", "microphone" })
        {
            capabilities[device] = await client.RequestAccessAsync(device, default);
            Console.WriteLine(device + " permission: " + capabilities[device].State);
        }
        File.WriteAllText(Path.Combine(output, "permissions.json"), JsonSerializer.Serialize(new { identity = client.Identity, capabilities }, new JsonSerializerOptions { WriteIndented = true }));
        using var camera = new CameraService(client);
        if (capabilities["camera"].CanExecute)
        {
            var devices = await camera.ListDevicesAsync(default);
            Check(devices.Count > 0, "独立应用发现真实相机设备");
            var images = new List<string>();
            for (var i = 0; i < 2; i++)
            {
                var path = await MediaFiles.CaptureAsync(camera, devices[0].Id, output, default);
                MediaFiles.RequirePng(path); images.Add(path);
            }
            Check(images.Distinct().Count() == 2, "两张真实 PNG 经本地 IPC 回传并分别提交");
            var before = Directory.GetFiles(output, "*.png").Length;
            try { await MediaFiles.CaptureAsync(camera, "SystemTools-Missing-Device", output, default); throw new Exception("Missing camera accepted"); }
            catch (NativeFeatureException) { Check(Directory.GetFiles(output, "*.png").Length == before, "不存在相机明确失败且不留下半成品"); }
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { await camera.CapturePngAsync(devices[0].Id, Path.Combine(output, "cancelled.png"), cancelled.Token); throw new Exception("Cancelled capture accepted"); }
            catch (OperationCanceledException) { Check(!File.Exists(Path.Combine(output, "cancelled.png")), "预取消不采集或提交图片"); }
            using var duringCapture = new CancellationTokenSource(100);
            try { await MediaFiles.CaptureAsync(camera, devices[0].Id, output, duringCapture.Token); throw new Exception("In-flight camera cancellation ignored"); }
            catch (OperationCanceledException)
            {
                status = await client.RequireAsync("session.status", null, default);
                Check(status.GetProperty("cameraJobs").GetInt32() == 0 && Directory.GetFiles(output, "*.png").Length == before,
                    "进行中的抓拍取消后释放相机会话且不提交半成品");
            }
        }
        if (capabilities["microphone"].CanExecute)
        {
            var id = Guid.NewGuid().ToString("D");
            await client.RequireAsync("microphone.prepare", new { id }, default);
            await client.RequireAsync("microphone.start", new { id }, default);
            var samples = new List<byte>();
            for (var i = 0; i < 25; i++)
            {
                await Task.Delay(50);
                var reply = await client.RequireAsync("microphone.poll", new { id }, default);
                samples.AddRange(Convert.FromBase64String(reply.GetProperty("pcm").GetString()!));
            }
            Check(samples.Count >= 16000 && samples.Count % 2 == 0, "真实麦克风转换为 16 kHz 单声道 PCM 并持续回传");
            await client.EndSessionAsync("microphone.release", id);
            File.WriteAllBytes(Path.Combine(output, "microphone.pcm"), samples.ToArray());
            if (Array.IndexOf(args, "--model") is var modelIndex && modelIndex >= 0)
            {
                using var decoder = new VoskDecoder(Path.GetFullPath("src/SystemTools.Platforms.MacOS/bin/Release/net10.0/libvosk.dylib"), args[modelIndex + 1]);
                foreach (var chunk in samples.Chunk(3200)) decoder.AcceptPcm16(chunk);
                decoder.Finish();
                Check(true, "助手实际采集的 PCM 经本机 Vosk 原生解码器处理");
            }
            status = await client.RequireAsync("session.status", null, default);
            Check(status.GetProperty("microphoneJobs").GetInt32() == 0, "真实麦克风停止、释放后无残留会话");
            await client.RequireAsync("microphone.prepare", new { id }, default);
            await client.RequireAsync("microphone.start", new { id }, default);
        }
        else Console.WriteLine("PENDING: microphone physical acceptance; permission/device unavailable.");
    }
    var pid = client.Identity.GetProperty("pid").GetInt32();
    var identity = client.Identity;
    client.Dispose();
    using (var process = TryProcess(pid))
    {
        if (process is not null) await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Check(process is null || process.HasExited, "插件断连后真实助手进程退出并释放所有设备");
    }
    File.WriteAllText(Path.Combine(output, args.Contains("--devices") ? "native-devices.json" : "native-preflight.json"),
        JsonSerializer.Serialize(new { passed = true, identity, capabilities, physicalDeviceAcceptance = args.Contains("--devices") && capabilities.Values.All(value => value.CanExecute), checks },
            new JsonSerializerOptions { WriteIndented = true }));
    return;
}

var temporary = Path.Combine("/tmp", "sttest-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
try
{
    var peers = new List<FakePeer>();
    var launches = 0;
    using var client = new CompanionClient(temporary, async (unusedApplication, path, token) =>
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        launches++;
        var session = JsonDocument.Parse(await File.ReadAllTextAsync(path, token)).RootElement.Clone();
        Check(File.GetUnixFileMode(Path.GetDirectoryName(path)!) == (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute), "会话目录仅当前用户可访问");
        Check(File.GetUnixFileMode(path) == (UnixFileMode.UserRead | UnixFileMode.UserWrite), "会话凭据仅当前用户可读写");
        var invalid = await FakePeer.ConnectAsync(session, new string('0', 64));
        _ = Task.Run(async () =>
        {
            await invalid.Closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            invalid.Dispose();
            Check(true, "错误会话令牌被拒绝且不占用有效连接");
            peers.Add(await FakePeer.ConnectAsync(session));
        });
    });
    using var preCancelled = new CancellationTokenSource(); preCancelled.Cancel();
    try { await client.CallAsync("echo", null, preCancelled.Token); throw new Exception("Pre-cancel ignored"); }
    catch (OperationCanceledException) { Check(launches == 0, "预取消不启动采集助手"); }
    var replies = await Task.WhenAll(Enumerable.Range(1, 12).Select(async number =>
    {
        var value = await client.RequireAsync("echo", new { number, delay = (13 - number) * 5 }, default);
        return value.GetProperty("number").GetInt32() == number;
    }));
    Check(replies.All(value => value) && launches == 1, "并发请求只启动一个助手且乱序回复不串线");
    Check(!client.Identity.TryGetProperty("token", out _), "诊断身份不包含会话令牌");
    using var cancel = new CancellationTokenSource(40);
    try { await client.RequireAsync("echo", new { number = 100, delay = 150 }, cancel.Token); throw new Exception("Cancel ignored"); }
    catch (OperationCanceledException) { Check(true, "进行中的请求可取消"); }
    var other = await client.RequireAsync("echo", new { number = 200, delay = 200 }, default);
    Check(other.GetProperty("number").GetInt32() == 200, "取消及迟到回复不影响其他消费者");
    var large = await client.RequireAsync("large", null, default);
    Check(large.GetProperty("payload").GetString()!.Length == 1024 * 1024, "真实本地 socket 正确传输大图像级别的消息");
    peers[0].Dispose();
    await peers[0].Closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
    await Task.Delay(50);
    try { await client.RequireAsync("echo", new { number = 300 }, default); throw new Exception("Stopped helper restarted automatically"); }
    catch (NativeFeatureException error) { Check(error.State == CapabilityState.Disabled && launches == 1, "助手退出后不因后台查询自动重启采集"); }
    Check((await client.RequestAccessAsync("camera", default)).CanExecute && launches == 2, "用户显式请求后可以重新连接助手");
    var first = client.Retain(); var second = client.Retain();
    first.Dispose();
    await client.RequireAsync("echo", new { number = 1 }, default);
    Check(true, "释放一个媒体消费者不关闭仍被使用的连接");
    second.Dispose();
    await peers[^1].Closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
    Check(true, "最后一个媒体消费者释放时关闭连接");
    foreach (var peer in peers) peer.Dispose();
    using var oversized = new CompanionClient(temporary, async (unusedApplication, path, token) =>
    {
        var session = JsonDocument.Parse(await File.ReadAllTextAsync(path, token)).RootElement.Clone();
        peers.Add(await FakePeer.ConnectAsync(session));
    });
    try { await oversized.CallAsync("oversized", null, default); throw new Exception("Oversized frame accepted"); }
    catch (IOException) { Check(true, "过大消息头明确失败并断开连接"); }
    foreach (var peer in peers) peer.Dispose();
    File.WriteAllText(Path.Combine(output, "ipc-tests.json"), JsonSerializer.Serialize(new { passed = true, checks }, new JsonSerializerOptions { WriteIndented = true }));
}
finally { Directory.Delete(temporary, recursive: true); }

static Process? TryProcess(int pid) { try { return Process.GetProcessById(pid); } catch (ArgumentException) { return null; } }

sealed class FakePeer : IDisposable
{
    private readonly Stream _stream;
    private readonly SemaphoreSlim _write = new(1);
    public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private FakePeer(Socket socket) { _stream = new NetworkStream(socket, ownsSocket: true); }
    public static async Task<FakePeer> ConnectAsync(JsonElement session, string? token = null)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(session.GetProperty("socket").GetString()!));
        var peer = new FakePeer(socket);
        await CompanionConnection.WriteFrameAsync(peer._stream, JsonSerializer.SerializeToUtf8Bytes(new { protocol = 1,
            token = token ?? session.GetProperty("token").GetString(), bundleId = CompanionClient.BundleIdentifier, pid = Environment.ProcessId, parentPid = 1 }), peer._write, default);
        _ = peer.RunAsync(); return peer;
    }
    private async Task RunAsync()
    {
        try
        {
            while (true)
            {
                using var doc = JsonDocument.Parse(await CompanionConnection.ReadFrameAsync(_stream, default));
                var request = doc.RootElement.Clone();
                if (request.GetProperty("operation").GetString() == "oversized")
                {
                    var header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, int.MaxValue);
                    await _stream.WriteAsync(header); continue;
                }
                _ = ReplyAsync(request);
            }
        }
        catch (Exception error) when (error is IOException or SocketException or ObjectDisposedException) { }
        finally { Closed.TrySetResult(); }
    }
    private async Task ReplyAsync(JsonElement request)
    {
        try
        {
            if (request.TryGetProperty("delay", out var delay)) await Task.Delay(delay.GetInt32());
            var result = new Dictionary<string, object?> { ["requestId"] = request.GetProperty("requestId").GetInt64(), ["state"] = "Available" };
            if (request.TryGetProperty("number", out var number)) result["number"] = number.GetInt32();
            if (request.GetProperty("operation").GetString() == "large") result["payload"] = new string('x', 1024 * 1024);
            await CompanionConnection.WriteFrameAsync(_stream, JsonSerializer.SerializeToUtf8Bytes(result), _write, default);
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException) { }
    }
    public void Dispose() => _stream.Dispose();
}
