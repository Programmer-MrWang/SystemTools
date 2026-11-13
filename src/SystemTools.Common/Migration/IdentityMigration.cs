using System.Text.Json;
using System.Text.Json.Nodes;
using SystemTools.Common.Storage;

namespace SystemTools.Common.Migration;

public enum ImportChoice { KeepExisting, ImportLegacy }
public sealed record MigrationRequest(string Source, string Target);
public sealed record MigrationPreview(MigrationRequest Request, string? SourceHash, string? TargetHash, string Candidate,
    string CandidateHash, bool Conflict, int ConvertedReferences);

public sealed class IdentityMigration
{
    private static readonly HashSet<string> IdentityFields = new(StringComparer.OrdinalIgnoreCase)
        { "id", "actionId", "triggerId", "componentId", "providerId", "pluginId", "ruleId", "themeId", "theme", "buttonId" };
    private static readonly HashSet<string> IdentityDictionaries = new(StringComparer.OrdinalIgnoreCase)
        { "enabledActions", "enabledTriggers", "enabledComponents", "enabledRules", "floatingWindowButtonRulesets" };
    private static readonly HashSet<string> IdentityArrays = new(StringComparer.OrdinalIgnoreCase)
        { "floatingWindowButtonOrder", "floatingWindowButtonRows", "buttonIds" };
    private readonly IReadOnlyDictionary<string, string> _mapping;
    public IdentityMigration(IReadOnlyDictionary<string, string>? mapping = null)
    {
        if (mapping is not null) { _mapping = mapping; return; }
        using var stream = typeof(IdentityMigration).Assembly.GetManifestResourceStream("SystemTools.IdentityMap.json")!;
        _mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
    public MigrationPreview Preview(MigrationRequest request)
    {
        request = new(Path.GetFullPath(request.Source), Path.GetFullPath(request.Target));
        RejectLinks(request.Source);
        RejectLinks(request.Target);
        var sourceHash = AtomicJsonStore<MigrationJournal>.Hash(request.Source) ?? throw new FileNotFoundException("迁移源文件不存在。", request.Source);
        var node = JsonNode.Parse(File.ReadAllText(request.Source)) as JsonObject ?? throw new InvalidDataException("迁移配置必须为 JSON 对象。" );
        var changes = Rewrite(node);
        var candidate = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var candidateHash = HashText(candidate);
        var targetHash = AtomicJsonStore<MigrationJournal>.Hash(request.Target);
        var same = File.Exists(request.Target) && JsonNode.DeepEquals(node, JsonNode.Parse(File.ReadAllText(request.Target)));
        return new(request, sourceHash, targetHash, candidate, candidateHash, targetHash is not null && !same, changes);
    }
    private int Rewrite(JsonNode? node, string? context = null)
    {
        var count = 0;
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(x => x.Key).ToArray())
            {
                if (IdentityDictionaries.Contains(context ?? "") && Map(key) is { } replacement && replacement != key)
                {
                    if (obj.ContainsKey(replacement)) throw new InvalidDataException("映射后的身份与现有字段冲突：" + replacement);
                    var value = obj[key]; obj.Remove(key); obj[replacement] = value; count++;
                    count += Rewrite(value, replacement);
                    continue;
                }
                if (IdentityFields.Contains(key) && obj[key] is JsonValue scalar && scalar.TryGetValue<string>(out var id) && Map(id) is { } mapped && mapped != id)
                { obj[key] = mapped; count++; }
                else count += Rewrite(obj[key], key);
            }
        }
        else if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                if (IdentityArrays.Contains(context ?? "") && array[i] is JsonValue scalar && scalar.TryGetValue<string>(out var id) && Map(id) is { } mapped && mapped != id)
                { array[i] = mapped; count++; }
                else count += Rewrite(array[i], context);
            }
        }
        return count;
    }
    private string? Map(string value)
    {
        if (_mapping.TryGetValue(value, out var mapped)) return mapped;
        if (Guid.TryParse(value, out var guid)) return _mapping.FirstOrDefault(x => Guid.TryParse(x.Key, out var old) && old == guid).Value;
        return null;
    }

    public string Apply(string targetRoot, IReadOnlyList<(MigrationPreview Preview, ImportChoice Choice)> selected)
    {
        targetRoot = Path.GetFullPath(targetRoot);
        RejectLinks(targetRoot);
        Directory.CreateDirectory(targetRoot);
        using var lease = new FileStream(Path.Combine(targetRoot, ".migration.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var items = selected.Where(x => x.Choice == ImportChoice.ImportLegacy).Select(x => x.Preview).ToArray();
        if (items.Select(x => x.Request.Target).Distinct(PathComparer).Count() != items.Length)
            throw new InvalidDataException("同一目标不能被多个迁移条目覆盖。" );
        foreach (var item in items)
        {
            EnsureWithin(targetRoot, item.Request.Target);
            RejectLinks(item.Request.Target);
            if (AtomicJsonStore<MigrationJournal>.Hash(item.Request.Source) != item.SourceHash ||
                AtomicJsonStore<MigrationJournal>.Hash(item.Request.Target) != item.TargetHash || HashText(item.Candidate) != item.CandidateHash)
                throw new ConfigurationConflictException("预览后文件已变化，请重新预览。" );
        }
        var folder = Path.Combine(targetRoot, ".migrations", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var journal = new MigrationJournal { Root = targetRoot, Status = "Prepared" };
        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            var backup = item.TargetHash is null ? null : Path.Combine(folder, index + ".before.json");
            if (backup is not null) File.Copy(item.Request.Target, backup, overwrite: false);
            var candidatePath = Path.Combine(folder, index + ".candidate.json");
            AtomicJsonStore<MigrationJournal>.AtomicWrite(candidatePath, item.Candidate);
            journal.Entries.Add(new() { Target = item.Request.Target, BeforeHash = item.TargetHash,
                AfterHash = item.CandidateHash, Backup = backup, Candidate = candidatePath });
        }
        var journalPath = Path.Combine(folder, "journal.json");
        WriteJournal(journalPath, journal);
        Resume(journalPath, leaseHeld: true);
        return journalPath;
    }

    public static void Resume(string journalPath) => Resume(journalPath, leaseHeld: false);
    private static void Resume(string journalPath, bool leaseHeld)
    {
        var journal = ReadJournal(journalPath);
        if (journal.Status == "RolledBack") throw new InvalidOperationException("已回退的迁移不能继续。" );
        using var lease = leaseHeld ? null : new FileStream(Path.Combine(journal.Root, ".migration.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        foreach (var entry in journal.Entries)
        {
            ValidateEntry(journalPath, journal.Root, entry);
            var current = AtomicJsonStore<MigrationJournal>.Hash(entry.Target);
            if (current == entry.AfterHash) continue;
            if (current != entry.BeforeHash) throw new ConfigurationConflictException("迁移目标已被修改，已暂停恢复。" );
            var candidate = File.ReadAllText(entry.Candidate);
            if (HashText(candidate) != entry.AfterHash) throw new InvalidDataException("迁移候选数据校验失败。" );
            AtomicJsonStore<MigrationJournal>.AtomicWrite(entry.Target, candidate);
            entry.Applied = true;
            WriteJournal(journalPath, journal);
        }
        journal.Status = "Committed";
        WriteJournal(journalPath, journal);
    }

    public static void Rollback(string journalPath)
    {
        var journal = ReadJournal(journalPath);
        using var lease = new FileStream(Path.Combine(journal.Root, ".migration.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        foreach (var entry in journal.Entries) ValidateEntry(journalPath, journal.Root, entry);
        foreach (var entry in journal.Entries.AsEnumerable().Reverse())
        {
            var current = AtomicJsonStore<MigrationJournal>.Hash(entry.Target);
            if (current == entry.BeforeHash) continue;
            if (current != entry.AfterHash) throw new ConfigurationConflictException("迁移后目标已被修改，回退已暂停以保留新数据。" );
            if (entry.Backup is null) File.Delete(entry.Target);
            else
            {
                if (AtomicJsonStore<MigrationJournal>.Hash(entry.Backup) != entry.BeforeHash) throw new InvalidDataException("迁移备份校验失败。" );
                AtomicJsonStore<MigrationJournal>.AtomicWriteBytes(entry.Target, File.ReadAllBytes(entry.Backup));
            }
            entry.Applied = false;
            WriteJournal(journalPath, journal);
        }
        journal.Status = "RolledBack";
        WriteJournal(journalPath, journal);
    }
    private static MigrationJournal ReadJournal(string path)
    {
        RejectLinks(path);
        var journal = JsonSerializer.Deserialize<MigrationJournal>(File.ReadAllText(path)) ?? throw new InvalidDataException("迁移日志为空。" );
        if (journal.Version != 1) throw new InvalidDataException("不支持此迁移日志版本。" );
        EnsureWithin(journal.Root, path);
        return journal;
    }
    private static void ValidateEntry(string journalPath, string root, MigrationEntry entry)
    {
        EnsureWithin(root, entry.Target);
        EnsureWithin(Path.GetDirectoryName(journalPath)!, entry.Candidate);
        RejectLinks(entry.Target); RejectLinks(entry.Candidate);
        if (entry.Backup is not null) { EnsureWithin(Path.GetDirectoryName(journalPath)!, entry.Backup); RejectLinks(entry.Backup); }
    }
    public static void EnsureWithin(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new IOException("迁移目标超出指定目录。" );
    }
    public static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("迁移不支持链接路径。" );
    }
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static string HashText(string text) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
    private static void WriteJournal(string path, MigrationJournal journal) => AtomicJsonStore<MigrationJournal>.AtomicWrite(path, JsonSerializer.Serialize(journal, new JsonSerializerOptions { WriteIndented = true }));
}

public sealed class MigrationJournal
{
    public int Version { get; set; } = 1;
    public string Root { get; set; } = "";
    public string Status { get; set; } = "";
    public List<MigrationEntry> Entries { get; set; } = [];
}
public sealed class MigrationEntry
{
    public string Target { get; set; } = "";
    public string? BeforeHash { get; set; }
    public string AfterHash { get; set; } = "";
    public string? Backup { get; set; }
    public string Candidate { get; set; } = "";
    public bool Applied { get; set; }
}
