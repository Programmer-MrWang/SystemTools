using System.Diagnostics;

namespace SystemTools.Common.Files;

public interface IDirectoryCopyBackend
{
    Task CopyAsync(string source, string target, CancellationToken cancellationToken);
}

public sealed class ManagedDirectoryCopyBackend : IDirectoryCopyBackend
{
    public Task CopyAsync(string source, string target, CancellationToken cancellationToken) => Task.Run(() => Copy(source, target, cancellationToken), cancellationToken);
    private static void Copy(string source, string target, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(target);
        foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos())
        {
            token.ThrowIfCancellationRequested();
            FileOperations.RejectLinks(entry.FullName);
            var destination = Path.Combine(target, entry.Name);
            if (entry is DirectoryInfo) Copy(entry.FullName, destination, token);
            else File.Copy(entry.FullName, destination);
        }
    }
}

public sealed class FileOperations(IDirectoryCopyBackend? directoryCopier = null)
{
    private readonly IDirectoryCopyBackend _copier = directoryCopier ?? new ManagedDirectoryCopyBackend();
    public async Task TransferAsync(string source, string destination, bool directory, bool move, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        RejectLinks(source); RejectLinks(destination);
        if (!directory)
        {
            if (!File.Exists(source)) throw new FileNotFoundException("源文件不存在。", source);
            if (Directory.Exists(destination)) destination = Path.Combine(destination, Path.GetFileName(source));
            RejectLinks(destination);
            if (SamePath(source, destination)) throw new IOException("源文件与目标文件相同。" );
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            token.ThrowIfCancellationRequested();
            await Task.Run(() => { if (move) File.Move(source, destination, overwrite: true); else File.Copy(source, destination, overwrite: true); }, token).ConfigureAwait(false);
            return;
        }
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException("源目录不存在。" );
        RejectRoot(source);
        var target = Path.Combine(destination, Path.GetFileName(source));
        RejectLinks(target);
        if (ContainsPath(source, target) || ContainsPath(target, source)) throw new IOException("源目录和目标目录不能相同或相互包含。" );
        ValidateTree(source, token);
        if (Directory.Exists(target)) ValidateTree(target, token);
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destination);
        var staging = Path.Combine(destination, "." + Path.GetFileName(source) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        var backup = staging + ".backup";
        var renamedSource = false;
        var installed = false;
        try
        {
            if (move)
            {
                try { Directory.Move(source, staging); renamedSource = true; }
                catch (IOException) when (Directory.Exists(source) && !Directory.Exists(staging)) { }
            }
            if (!renamedSource) await _copier.CopyAsync(source, staging, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (Directory.Exists(target)) Directory.Move(target, backup);
            try { Directory.Move(staging, target); installed = true; }
            catch { if (Directory.Exists(backup) && !Directory.Exists(target)) Directory.Move(backup, target); throw; }
            // Once installed, finish the commit without cancellation so a move keeps its target.
            if (move && !renamedSource) Directory.Delete(source, recursive: true);
            if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
        }
        finally
        {
            if (!installed && renamedSource && Directory.Exists(staging)) Directory.Move(staging, source);
            else if (!installed && Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }
    public Task DeleteAsync(string target, bool directory, CancellationToken token = default)
    {
        target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        token.ThrowIfCancellationRequested(); RejectLinks(target);
        if (directory) { RejectRoot(target); ValidateTree(target, token); }
        else if (!File.Exists(target)) throw new FileNotFoundException("目标文件不存在。", target);
        return Task.Run(() => { token.ThrowIfCancellationRequested(); if (directory) Directory.Delete(target, recursive: true); else File.Delete(target); }, token);
    }
    internal static void ValidateTree(string path, CancellationToken token)
    {
        RejectLinks(path);
        foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
        {
            token.ThrowIfCancellationRequested(); RejectLinks(entry.FullName);
            if (entry is DirectoryInfo) ValidateTree(entry.FullName, token);
        }
    }
    public static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("文件操作不支持符号链接或目录联接，请选择实际路径。" );
    }
    private static void RejectRoot(string path)
    {
        if (new DirectoryInfo(path).Parent is null) throw new IOException("不能操作磁盘或共享根目录。" );
    }
    private static StringComparison Comparison => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static bool SamePath(string a, string b) => string.Equals(a, b, Comparison);
    private static bool ContainsPath(string parent, string child) => SamePath(parent, child) || child.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, Comparison);
}
