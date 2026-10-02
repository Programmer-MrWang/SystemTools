using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace SystemTools.Shared;

internal static class FaceDependencyInstaller
{
    public static void InstallArchive(string archivePath, string dependencyRoot)
    {
        // Keep staging on the same volume so replacing files uses renames.
        var stagingPath = Path.Combine(dependencyRoot, $".FaceModels.extracting-{Guid.NewGuid():N}");
        try
        {
            ZipFile.ExtractToDirectory(archivePath, stagingPath);
            var sourceRoot = Directory.EnumerateDirectories(stagingPath, "*", SearchOption.AllDirectories)
                .Prepend(stagingPath)
                .FirstOrDefault(DependencyPaths.HasFaceRecognitionDependencies)
                ?? throw new InvalidDataException("压缩包中找不到完整的人脸识别依赖文件。");

            InstallFiles(sourceRoot, dependencyRoot);
        }
        finally
        {
            TryDeleteDirectory(stagingPath);
        }
    }

    private static void InstallFiles(string sourceRoot, string dependencyRoot)
    {
        var backupPath = Path.Combine(dependencyRoot, $".FaceModels.backup-{Guid.NewGuid():N}");
        var changes = new List<(string Destination, string Backup, bool HadExisting, bool Installed)>();
        var createdDirectories = new List<string>();
        var canDeleteBackup = false;
        try
        {
            foreach (var sourceDirectory in Directory.GetDirectories(sourceRoot, "*", SearchOption.AllDirectories)
                         .OrderBy(path => path.Length))
            {
                var destinationDirectory = Path.Combine(dependencyRoot, Path.GetRelativePath(sourceRoot, sourceDirectory));
                if (!Directory.Exists(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                    createdDirectories.Add(destinationDirectory);
                }
            }

            // Only replace files supplied by the archive. Other dependencies may share runtimes/.
            foreach (var source in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var relativePath = Path.GetRelativePath(sourceRoot, source);
                var destination = Path.Combine(dependencyRoot, relativePath);
                var backup = Path.Combine(backupPath, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var hadExisting = File.Exists(destination);
                if (hadExisting)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Move(destination, backup);
                }

                changes.Add((destination, backup, hadExisting, false));
                File.Move(source, destination);
                changes[^1] = (destination, backup, hadExisting, true);
            }

            if (!DependencyPaths.HasFaceRecognitionDependencies(dependencyRoot))
            {
                throw new InvalidDataException("安装后人脸识别依赖文件不完整。");
            }

            canDeleteBackup = true;
        }
        catch (Exception installError)
        {
            var rollbackErrors = new List<Exception>();
            for (var i = changes.Count - 1; i >= 0; i--)
            {
                var change = changes[i];
                try
                {
                    // File.Delete does not remove a conflicting directory.
                    if (change.Installed && File.Exists(change.Destination))
                    {
                        File.Delete(change.Destination);
                    }
                    if (change.HadExisting)
                    {
                        File.Move(change.Backup, change.Destination);
                    }
                }
                catch (Exception rollbackError)
                {
                    rollbackErrors.Add(rollbackError);
                }
            }

            for (var i = createdDirectories.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(createdDirectories[i]).Any())
                    {
                        Directory.Delete(createdDirectories[i]);
                    }
                }
                catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException)
                {
                    Debug.WriteLine($"[SystemTools] 临时安装目录清理失败: {cleanupError.Message}");
                }
            }

            canDeleteBackup = rollbackErrors.Count == 0;
            if (!canDeleteBackup)
            {
                // Never delete the only remaining originals when rollback itself fails.
                throw new AggregateException($"依赖安装失败，未恢复的原始文件保留在 {backupPath}",
                    new[] { installError }.Concat(rollbackErrors));
            }

            throw;
        }
        finally
        {
            if (canDeleteBackup)
            {
                TryDeleteDirectory(backupPath);
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[SystemTools] 临时依赖文件清理失败: {path}: {ex.Message}");
        }
    }
}
