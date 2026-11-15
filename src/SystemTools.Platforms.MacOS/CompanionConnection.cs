using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text.Json;

namespace SystemTools.Platforms.MacOS;

// One authenticated local connection; cancellation removes only that request, never another caller's reply.
internal sealed class CompanionConnection : IDisposable
{
    internal const int MaximumFrame = 24 * 1024 * 1024;
    private readonly Stream _stream;
    private readonly SemaphoreSlim _write = new(1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly CancellationTokenSource _stop = new();
    private long _sequence;
    internal bool IsConnected => !_stop.IsCancellationRequested;

    internal CompanionConnection(Socket socket)
    {
        _stream = new NetworkStream(socket, ownsSocket: true);
        _ = ReadRepliesAsync();
    }

    internal async Task<JsonElement> CallAsync(string operation, object? arguments, CancellationToken token,
        TimeSpan? timeout = null)
    {
        token.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(10));
        var id = Interlocked.Increment(ref _sequence);
        var reply = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, reply)) throw new IOException("采集会话请求重复。");
        try
        {
            var fields = arguments is null ? new Dictionary<string, JsonElement>() :
                JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(arguments))!;
            fields["requestId"] = JsonSerializer.SerializeToElement(id);
            fields["operation"] = JsonSerializer.SerializeToElement(operation);
            await WriteFrameAsync(_stream, JsonSerializer.SerializeToUtf8Bytes(fields), _write, deadline.Token).ConfigureAwait(false);
            return await reply.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            Dispose();
            throw new IOException("采集助手连接中断或响应超时，请重新检查设备。");
        }
        finally { _pending.TryRemove(id, out _); }
    }

    private async Task ReadRepliesAsync()
    {
        Exception? failure = null;
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using var document = JsonDocument.Parse(await ReadFrameAsync(_stream, _stop.Token).ConfigureAwait(false));
                var value = document.RootElement;
                var id = value.GetProperty("requestId").GetInt64();
                if (_pending.TryRemove(id, out var completion)) completion.TrySetResult(value.Clone());
            }
        }
        catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or JsonException or InvalidOperationException or KeyNotFoundException or ObjectDisposedException or FormatException)
        { failure = error; }
        finally
        {
            Dispose();
            foreach (var item in _pending)
                if (_pending.TryRemove(item.Key, out var completion))
                    completion.TrySetException(new IOException("采集助手已断开，设备采集已停止。", failure));
        }
    }

    internal static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken token)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, token).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is <= 0 or > MaximumFrame) throw new IOException("采集助手消息长度无效。");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        return bytes;
    }

    internal static async Task WriteFrameAsync(Stream stream, byte[] bytes, SemaphoreSlim gate, CancellationToken token)
    {
        if (bytes.Length is <= 0 or > MaximumFrame) throw new IOException("采集助手消息过大。");
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var header = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(header, bytes.Length);
            await stream.WriteAsync(header, token).ConfigureAwait(false);
            await stream.WriteAsync(bytes, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
        }
        catch { stream.Dispose(); throw; }
        finally { gate.Release(); }
    }

    public void Dispose()
    {
        if (_stop.IsCancellationRequested) return;
        _stop.Cancel();
        _stream.Dispose();
    }
}
