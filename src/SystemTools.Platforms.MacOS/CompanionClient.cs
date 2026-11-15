using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using SystemTools.Contracts;

namespace SystemTools.Platforms.MacOS;

internal sealed class CompanionClient : IDisposable
{
    internal const string BundleName = "SystemTools Companion.app";
    internal const string BundleIdentifier = "io.github.programmer-mrwang.systemtools.companion";
    private readonly string _application;
    private readonly Func<string, string, CancellationToken, Task> _launch;
    private readonly SemaphoreSlim _start = new(1);
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<string, Capability> _capabilities = new();
    private CompanionConnection? _connection;
    private string? _directory;
    private Task? _refresh;
    private DateTime _nextRefresh;
    private int _owners;
    private int _disposed;
    internal JsonElement Identity { get; private set; }

    internal CompanionClient(string? application = null, Func<string, string, CancellationToken, Task>? launch = null)
    {
        var root = Path.GetDirectoryName(typeof(Backend).Assembly.Location)!;
        _application = application ?? new[] { Path.Combine(root, "platforms/osx-arm64", BundleName), Path.Combine(root, BundleName) }
            .FirstOrDefault(Directory.Exists) ?? Path.Combine(root, "platforms/osx-arm64", BundleName);
        _launch = launch ?? LaunchAsync;
    }

    internal IDisposable Retain()
    {
        Interlocked.Increment(ref _owners);
        return new Ownership(this);
    }

    private sealed class Ownership(CompanionClient owner) : IDisposable
    {
        private CompanionClient? _owner = owner;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _owner, null) is { } value && Interlocked.Decrement(ref value._owners) == 0)
                value.Dispose();
        }
    }

    internal Capability Probe(string device)
    {
        if (Volatile.Read(ref _disposed) != 0) return new(CapabilityState.Disabled, "采集助手已停止。");
        if (!Directory.Exists(_application))
            return new(CapabilityState.MissingDependency, "SystemTools 采集助手缺失，请重新安装完整插件包。");
        lock (_capabilities)
        {
            if ((_refresh is null || _refresh.IsCompleted) && DateTime.UtcNow >= _nextRefresh)
            {
                _nextRefresh = DateTime.UtcNow.AddSeconds(2);
                _refresh = RefreshAsync();
            }
        }
        if (_connection is { IsConnected: false }) return new(CapabilityState.Disabled, "采集助手已停止，请点击申请设备权限重新连接。");
        return _capabilities.TryGetValue(device, out var state) ? state :
            new(CapabilityState.PermissionRequired, "正在连接 SystemTools 采集助手，请稍后重新检查；首次使用请申请设备权限。");
    }

    internal async Task<Capability> ProbeAsync(string device, CancellationToken token) =>
        _capabilities[device] = NativeBridge.Status(await CallAsync(device + ".probe", null, token).ConfigureAwait(false));

    private async Task RefreshAsync()
    {
        try
        {
            foreach (var device in new[] { "camera", "microphone" })
                _capabilities[device] = NativeBridge.Status(await CallAsync(device + ".probe", null, _stop.Token).ConfigureAwait(false));
        }
        catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or NativeFeatureException or
            UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            var state = new Capability(CapabilityState.MissingDependency, error is NativeFeatureException ? error.Message : "采集助手未能连接，请重新检查或重新安装插件。");
            foreach (var device in new[] { "camera", "microphone" }) _capabilities[device] = state;
        }
    }

    internal async Task<Capability> RequestAccessAsync(string device, CancellationToken token)
    {
        await _start.WaitAsync(token).ConfigureAwait(false);
        try { if (_connection is { IsConnected: false }) _connection = null; }
        finally { _start.Release(); }
        var response = await CallAsync(device + ".request", null, token, TimeSpan.FromSeconds(65)).ConfigureAwait(false);
        return _capabilities[device] = NativeBridge.Status(response);
    }

    internal async Task<JsonElement> CallAsync(string operation, object? arguments, CancellationToken token, TimeSpan? timeout = null)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        var connection = await ConnectAsync(linked.Token).ConfigureAwait(false);
        return await connection.CallAsync(operation, arguments, linked.Token, timeout).ConfigureAwait(false);
    }

    internal async Task<JsonElement> RequireAsync(string operation, object? arguments, CancellationToken token, TimeSpan? timeout = null)
    {
        var response = await CallAsync(operation, arguments, token, timeout).ConfigureAwait(false);
        var state = NativeBridge.Status(response);
        if (!state.CanExecute) throw new NativeFeatureException(state.State, state.Reason);
        return response;
    }

    // Cleanup must stay on the original connection: reconnecting would create an unrelated device session.
    internal async Task EndSessionAsync(string operation, string id)
    {
        var connection = _connection;
        if (connection?.IsConnected != true) return;
        try
        {
            var reply = await connection.CallAsync(operation, new { id }, _stop.Token, TimeSpan.FromSeconds(25)).ConfigureAwait(false);
            if (!NativeBridge.Status(reply).CanExecute) connection.Dispose();
        }
        catch { connection.Dispose(); }
    }

    private async Task<CompanionConnection> ConnectAsync(CancellationToken token)
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Mac 采集助手需要 macOS。");
        token.ThrowIfCancellationRequested();
        if (_connection?.IsConnected == true) return _connection;
        await _start.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_connection?.IsConnected == true) return _connection;
            if (_connection is not null) throw new NativeFeatureException(CapabilityState.Disabled, "采集助手已停止，请点击申请设备权限重新连接。");
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (!Directory.Exists(_application)) throw new NativeFeatureException(CapabilityState.MissingDependency, "SystemTools 采集助手缺失，请重新安装完整插件包。");
            CleanDirectory();
            // Darwin's Unix socket path limit is 104 bytes, so use a short, private directory.
            _directory = Path.Combine("/tmp", "stc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var socketPath = Path.Combine(_directory, "ipc");
            var sessionPath = Path.Combine(_directory, "session.json");
            var secret = RandomNumberGenerator.GetBytes(32);
            using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(socketPath));
            File.SetUnixFileMode(socketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            listener.Listen(4);
            await File.WriteAllTextAsync(sessionPath, JsonSerializer.Serialize(new { protocol = 1, socket = socketPath,
                token = Convert.ToHexString(secret), hostPid = Environment.ProcessId }), token).ConfigureAwait(false);
            File.SetUnixFileMode(sessionPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            await _launch(_application, sessionPath, deadline.Token).ConfigureAwait(false);
            while (true)
            {
                var socket = await listener.AcceptAsync(deadline.Token).ConfigureAwait(false);
                try
                {
                    using var helloTimeout = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                    helloTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                    using var stream = new NetworkStream(socket, ownsSocket: false);
                    using var hello = JsonDocument.Parse(await CompanionConnection.ReadFrameAsync(stream, helloTimeout.Token).ConfigureAwait(false));
                    var identity = hello.RootElement;
                    var received = Convert.FromHexString(identity.GetProperty("token").GetString()!);
                    if (identity.GetProperty("protocol").GetInt32() != 1 ||
                        identity.GetProperty("bundleId").GetString() != BundleIdentifier ||
                        !CryptographicOperations.FixedTimeEquals(received, secret))
                    { socket.Dispose(); continue; }
                    Identity = JsonSerializer.SerializeToElement(new { bundleId = identity.GetProperty("bundleId").GetString(),
                        pid = identity.GetProperty("pid").GetInt32(), parentPid = identity.GetProperty("parentPid").GetInt32(), protocol = 1 });
                    File.Delete(sessionPath);
                    _connection = new CompanionConnection(socket);
                    _ = HeartbeatAsync(_connection);
                    return _connection;
                }
                catch (Exception error) when (error is IOException or SocketException or JsonException or FormatException or InvalidOperationException or KeyNotFoundException or OperationCanceledException)
                { socket.Dispose(); deadline.Token.ThrowIfCancellationRequested(); }
            }
        }
        catch { CleanDirectory(); throw; }
        finally { _start.Release(); }
    }

    private async Task HeartbeatAsync(CompanionConnection connection)
    {
        try
        {
            while (connection.IsConnected)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), _stop.Token).ConfigureAwait(false);
                await connection.CallAsync("session.ping", null, _stop.Token, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
        }
        catch { connection.Dispose(); }
    }

    private static async Task LaunchAsync(string application, string session, CancellationToken token)
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Mac 采集助手需要 macOS。");
        var executable = Path.Combine(application, "Contents/MacOS/SystemToolsCompanion");
        if (!File.Exists(executable) || new FileInfo(executable).LinkTarget is not null)
            throw new NativeFeatureException(CapabilityState.MissingDependency, "采集助手不完整，请重新安装完整插件包。");
        // .NET ZIP extraction does not restore Unix executable bits; only repair our own bundled executable.
        File.SetUnixFileMode(executable, File.GetUnixFileMode(executable) | UnixFileMode.UserExecute);
        var result = await Task.Run(() => NativeBridge.Call(new { operation = "companion.launch", application, session }), token).ConfigureAwait(false);
        var status = NativeBridge.Status(result);
        if (!status.CanExecute) throw new NativeFeatureException(status.State, status.Reason);
    }

    private void CleanDirectory()
    {
        var directory = Interlocked.Exchange(ref _directory, null);
        if (directory is null) return;
        foreach (var name in new[] { "session.json", "ipc" })
            try { File.Delete(Path.Combine(directory, name)); } catch (IOException) { }
        try { Directory.Delete(directory); } catch (IOException) { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        _connection?.Dispose();
        CleanDirectory();
    }
}
