using System.Text.Json;

namespace SystemTools.Common.Storage;

public sealed record ProfileImportReport(string[] Imported, string[] Conflicts, string[] Invalid);

public static class PersistentProfileImport
{
    public static ProfileImportReport CopyOnce(string source, string destination)
    {
        source = Path.GetFullPath(source);
        destination = Path.GetFullPath(destination);
        if (source == destination || !Directory.Exists(source)) return new([], [], []);
        SystemTools.Common.Migration.IdentityMigration.RejectLinks(source);
        SystemTools.Common.Migration.IdentityMigration.RejectLinks(destination);
        Directory.CreateDirectory(destination);
        var journalPath = Path.Combine(destination, ".legacy-import.json");
        var journal = new AtomicJsonStore<ImportJournal>(journalPath);
        var state = journal.Load();
        var imported = new List<string>();
        var conflicts = new List<string>();
        var invalid = new List<string>();
        foreach (var path in Directory.EnumerateFiles(source, "*.json"))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("不迁移链接文件：" + Path.GetFileName(path));
            var name = Path.GetFileName(path);
            if (state.Completed.Contains(name)) continue;
            var target = Path.Combine(destination, name);
            if (File.Exists(target) && AtomicJsonStore<ImportJournal>.Hash(path) != AtomicJsonStore<ImportJournal>.Hash(target))
            {
                conflicts.Add(name);
                continue;
            }
            try { using (JsonDocument.Parse(File.ReadAllText(path))) { } }
            catch (JsonException) { invalid.Add(name); continue; }
            if (!File.Exists(target)) File.Copy(path, target, overwrite: false);
            state.Completed.Add(name);
            journal.Save(state);
            imported.Add(name);
        }
        var report = new ProfileImportReport(imported.ToArray(), conflicts.ToArray(), invalid.ToArray());
        AtomicJsonStore<ImportJournal>.AtomicWrite(Path.Combine(destination, ".legacy-import-report.json"), JsonSerializer.Serialize(report));
        return report;
    }

    public sealed class ImportJournal { public HashSet<string> Completed { get; set; } = new(StringComparer.Ordinal); }
}
