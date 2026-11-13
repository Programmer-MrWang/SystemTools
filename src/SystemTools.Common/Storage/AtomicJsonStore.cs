using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SystemTools.Common.Storage;

public sealed class ConfigurationConflictException(string message) : IOException(message);

/// <summary>One document per instance; stale writers fail instead of overwriting another writer.</summary>
public sealed class AtomicJsonStore<T> where T : class, new()
{
    public const int SchemaVersion = 1;
    private readonly string _path;
    private readonly Func<T, string> _serialize;
    private readonly Func<string, T> _deserialize;
    private readonly object _gate = new();
    private string? _expectedHash;
    private JsonNode? _original;
    private JsonNode? _baseline;
    private bool _loaded;
    private bool _recovered;
    public string? RecoveryNotice { get; private set; }

    public AtomicJsonStore(string path, Func<T, string>? serialize = null, Func<string, T>? deserialize = null)
    {
        _path = Path.GetFullPath(path);
        _serialize = serialize ?? (value => JsonSerializer.Serialize(value));
        _deserialize = deserialize ?? (json => JsonSerializer.Deserialize<T>(json) ?? throw new JsonException("配置为空。"));
    }
    public T Load()
    {
        lock (_gate)
        {
            _expectedHash = Hash(_path);
            string? json = File.Exists(_path) ? File.ReadAllText(_path) : null;
            T value;
            try { value = Decode(json); }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                if (!File.Exists(_path + ".bak")) throw new IOException("配置损坏且无可用备份，原文件已保留。", ex);
                var backup = File.ReadAllText(_path + ".bak");
                value = Decode(backup);
                File.Copy(_path, _path + ".corrupt-" + Guid.NewGuid().ToString("N"));
                json = backup;
                _recovered = true;
                RecoveryNotice = "已读取备份，损坏的原文件已另存。";
            }
            _original = json is null ? new JsonObject() : JsonNode.Parse(json);
            ValidateVersion(_original);
            _baseline = JsonNode.Parse(_serialize(value));
            _loaded = true;
            return value;
        }
    }
    private T Decode(string? json)
    {
        if (json is null) return new T();
        var node = JsonNode.Parse(json);
        if (node is not JsonObject) throw new JsonException("配置必须为对象。" );
        ValidateVersion(node);
        return _deserialize(json);
    }
    private static void ValidateVersion(JsonNode? node)
    {
        if (node is JsonObject obj && obj["_systemToolsSchema"] is JsonValue version &&
            (!version.TryGetValue<int>(out var number) || number > SchemaVersion || number < 0))
            throw new InvalidDataException("配置版本较新或版本号无效，已保留原文件，请使用兼容版本。" );
    }
    public void Save(T value)
    {
        lock (_gate)
        {
            if (!_loaded) Load();
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            using var lease = new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (_expectedHash != Hash(_path)) throw new ConfigurationConflictException("配置已被其他写入者修改，请重新加载后保存。" );
            var current = JsonNode.Parse(_serialize(value)) as JsonObject ?? throw new JsonException("配置必须为对象。" );
            PreserveUnknown(_original, _baseline, current);
            current["_systemToolsSchema"] = SchemaVersion;
            var json = current.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            if (!_recovered && File.Exists(_path)) AtomicWrite(_path + ".bak", File.ReadAllText(_path));
            AtomicWrite(_path, json);
            _expectedHash = Hash(_path);
            _original = current.DeepClone();
            _baseline = JsonNode.Parse(_serialize(value));
            _recovered = false;
        }
    }
    public static void AtomicWrite(string path, string text)
        => AtomicWriteBytes(path, Encoding.UTF8.GetBytes(text));

    public static void AtomicWriteBytes(string path, byte[] bytes)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".writing-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static string? Hash(string path) => File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : null;

    private static void PreserveUnknown(JsonNode? original, JsonNode? baseline, JsonNode? current)
    {
        if (original is JsonObject oldObject && baseline is JsonObject known && current is JsonObject target)
        {
            foreach (var pair in oldObject)
            {
                if (!known.ContainsKey(pair.Key) && !target.ContainsKey(pair.Key)) target[pair.Key] = pair.Value?.DeepClone();
                else if (target.ContainsKey(pair.Key)) PreserveUnknown(pair.Value, known[pair.Key], target[pair.Key]);
            }
        }
        else if (original is JsonArray oldArray && baseline is JsonArray oldKnown && current is JsonArray newArray)
        {
            for (var i = 0; i < newArray.Count; i++)
            {
                var id = Identity(newArray[i]);
                var oldIndex = id is null ? i : Enumerable.Range(0, oldArray.Count).FirstOrDefault(n => Identity(oldArray[n]) == id, -1);
                var knownIndex = id is null ? i : Enumerable.Range(0, oldKnown.Count).FirstOrDefault(n => Identity(oldKnown[n]) == id, -1);
                if (oldIndex >= 0 && oldIndex < oldArray.Count && knownIndex >= 0 && knownIndex < oldKnown.Count)
                    PreserveUnknown(oldArray[oldIndex], oldKnown[knownIndex], newArray[i]);
            }
        }
    }
    private static string? Identity(JsonNode? node) => node is JsonObject obj
        ? obj.FirstOrDefault(x => x.Key.Equals("id", StringComparison.OrdinalIgnoreCase)).Value?.ToJsonString() : null;
}
