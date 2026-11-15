using System.Security.Cryptography;
using System.Text.Json;

namespace SystemTools.Common.Migration;

/// <summary>Captures plugin-owned data before a new binary version can write it. Existing snapshots are never replaced.</summary>
public static class UpgradeSnapshot
{
    public static string Ensure(string configFolder, string version)
    {
        configFolder = Path.GetFullPath(configFolder);
        IdentityMigration.RejectLinks(configFolder);
        if (string.IsNullOrWhiteSpace(version) || version is "." or ".." || version.Length > 80 || version.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-')))
            throw new ArgumentException("升级版本标识无效。");
        Directory.CreateDirectory(configFolder);
        var root = Path.Combine(configFolder, ".upgrade-snapshots");
        IdentityMigration.RejectLinks(root);
        Directory.CreateDirectory(root);
        PrivateDirectory(root);
        IdentityMigration.RejectLinks(Path.Combine(root, ".lock"));
        using var gate = new FileStream(Path.Combine(root, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var destination = Path.Combine(root, version);
        if (Directory.Exists(destination)) { Validate(destination); return destination; }
        var temporary = destination + ".preparing-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(temporary); PrivateDirectory(temporary);
        try
        {
            var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
            long bytes = 0;
            void Copy(string folder)
            {
                IdentityMigration.RejectLinks(folder);
                foreach (var path in Directory.EnumerateFileSystemEntries(folder))
                {
                    IdentityMigration.RejectLinks(path);
                    var relative = Path.GetRelativePath(configFolder, path);
                    if (relative.Split(Path.DirectorySeparatorChar)[0] == ".upgrade-snapshots" || path.EndsWith(".lock", StringComparison.Ordinal)) continue;
                    if (Directory.Exists(path)) { Copy(path); continue; }
                    var info = new FileInfo(path);
                    bytes = checked(bytes + info.Length);
                    if (bytes > 1_073_741_824 || hashes.Count >= 20_000) throw new IOException("升级快照超过限制，请先归档历史数据。");
                    var target = Path.Combine(temporary, "files", relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!); PrivateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(path, target, overwrite: false); PrivateFile(target);
                    var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target)));
                    if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) != hash) throw new IOException("备份期间配置发生变化，请退出其他宿主再重试。");
                    hashes[relative.Replace(Path.DirectorySeparatorChar, '/')] = hash;
                }
            }
            Copy(configFolder);
            var current = new SortedDictionary<string, string>(StringComparer.Ordinal);
            void Scan(string folder)
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(folder))
                {
                    IdentityMigration.RejectLinks(path);
                    var relative = Path.GetRelativePath(configFolder, path);
                    if (relative.Split(Path.DirectorySeparatorChar)[0] == ".upgrade-snapshots" || path.EndsWith(".lock", StringComparison.Ordinal)) continue;
                    if (Directory.Exists(path)) { Scan(path); continue; }
                    using var stream = File.OpenRead(path);
                    current[relative.Replace(Path.DirectorySeparatorChar, '/')] = Convert.ToHexString(SHA256.HashData(stream));
                }
            }
            Scan(configFolder);
            if (!current.SequenceEqual(hashes)) throw new IOException("备份期间配置集合发生变化，请退出其他宿主再重试。");
            var manifest = new SnapshotManifest(1, version, hashes);
            File.WriteAllText(Path.Combine(temporary, "snapshot.json"), JsonSerializer.Serialize(manifest));
            PrivateFile(Path.Combine(temporary, "snapshot.json"));
            Directory.Move(temporary, destination);
            Validate(destination);
            return destination;
        }
        catch { if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true); throw; }
    }
    public static void Validate(string snapshot)
    {
        IdentityMigration.RejectLinks(snapshot);
        IdentityMigration.RejectLinks(Path.Combine(snapshot, "snapshot.json"));
        var manifest = JsonSerializer.Deserialize<SnapshotManifest>(File.ReadAllText(Path.Combine(snapshot, "snapshot.json"))) ?? throw new IOException("升级快照为空。");
        if (manifest.SchemaVersion != 1) throw new IOException("升级快照版本不支持。");
        foreach (var (relative, hash) in manifest.Files)
        {
            var path = Path.GetFullPath(Path.Combine(snapshot, "files", relative));
            IdentityMigration.EnsureWithin(Path.Combine(snapshot, "files"), path); IdentityMigration.RejectLinks(path);
            if (!File.Exists(path) || Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) != hash)
                throw new IOException("升级快照损坏，已停止覆盖数据。");
        }
    }
    private static void PrivateDirectory(string path) { if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
    private static void PrivateFile(string path) { if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
    public sealed record SnapshotManifest(int SchemaVersion, string Version, SortedDictionary<string, string> Files);
}
