using ClassIsland.Shared.Helpers;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ClassIsland.Shared;
using SystemTools.Shared;

namespace SystemTools.ConfigHandlers;

public class MainConfigHandler
{
    readonly string _configPath;
    private readonly ILogger<MainConfigHandler>? _logger;
    private readonly List<(string Message, string ExceptionType)> _pendingMigrationWarnings = [];
    public MainConfigData Data { get; set; }

    public MainConfigHandler(string pluginConfigFolder)
        : this(pluginConfigFolder, null)
    {
    }

    public MainConfigHandler(string pluginConfigFolder, ILogger<MainConfigHandler>? logger)
    {
        _configPath = Path.Combine(pluginConfigFolder, "Main.json");
        _logger = logger ?? IAppHost.TryGetService<ILogger<MainConfigHandler>>();

        Data = ConfigureFileHelper.LoadConfig<MainConfigData>(_configPath);

        if (Data.RequiresCredentialMigration)
        {
            var migrationPath = _configPath + ".migrating-" + Guid.NewGuid().ToString("N");
            try
            {
                // The host helper truncates the destination before writing. Stage
                // migration first so disk/write failures cannot damage the old file.
                ConfigureFileHelper.SaveConfig(migrationPath, Data);
                File.Move(migrationPath, _configPath, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
            {
                // A readable profile should remain usable if migration cannot be
                // saved. Serialization still encrypts the key on any later save.
                LogMigrationFailure(ex, "无法保存加密后的配置密钥，已保留原配置。");
            }
            finally
            {
                TryDeleteMigrationFile(migrationPath);
            }
        }
        MigrateBackupCredential();

        SubscribeToChanges();

        GlobalConstants.MainConfig = this;
    }
    

    void SubscribeToChanges()
    {
        Data.PropertyChanged += (sender, args) => { Save(); };
    }

    public void Save()
    {
        ConfigureFileHelper.SaveConfig(_configPath, Data);
    }

    internal void LogPendingMigrationWarnings(ILogger logger)
    {
        foreach (var warning in _pendingMigrationWarnings)
        {
            logger.LogWarning("[SystemTools] {Message} 异常类型：{ExceptionType}",
                warning.Message, warning.ExceptionType);
        }

        _pendingMigrationWarnings.Clear();
    }

    private void MigrateBackupCredential()
    {
        var backupPath = _configPath + ".bak";
        string? temporaryPath = null;
        try
        {
            if (!File.Exists(backupPath)) return;

            // Backups may contain older settings when host backups are disabled.
            // Update only the key, preserving every other field and any unknown data.
            if (JsonNode.Parse(File.ReadAllText(backupPath)) is not JsonObject backup ||
                backup["aiApiKey"] is not JsonValue value ||
                !value.TryGetValue<string>(out var apiKey) ||
                string.IsNullOrEmpty(apiKey) || CredentialProtection.IsProtected(apiKey))
            {
                return;
            }

            backup["aiApiKey"] = CredentialProtection.Protect(apiKey);
            temporaryPath = backupPath + ".migrating-" + Guid.NewGuid().ToString("N");
            ConfigureFileHelper.SaveConfig(temporaryPath, backup);
            File.Move(temporaryPath, backupPath, true);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or IOException or UnauthorizedAccessException or CryptographicException)
        {
            // Leave an unreadable/unwritable backup intact, without disclosing keys
            // or turning a backup migration error into a plugin startup failure.
            LogMigrationFailure(ex, "无法更新配置备份中的密钥，已保留原备份。");
        }
        finally
        {
            if (temporaryPath != null)
            {
                TryDeleteMigrationFile(temporaryPath);
            }
        }
    }

    private static void TryDeleteMigrationFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine("[SystemTools] 无法清理配置迁移临时文件。");
        }
    }

    private void LogMigrationFailure(Exception exception, string message)
    {
        if (_logger is not null)
        {
            // Do not pass the exception object through: parser and provider
            // exceptions can include configuration fragments in their messages.
            _logger.LogWarning("[SystemTools] {Message} 异常类型：{ExceptionType}",
                message, exception.GetType().Name);
            return;
        }

        _pendingMigrationWarnings.Add((message, exception.GetType().Name));

        // The first plugin initialization can happen before the host logger is available.
        Console.Error.WriteLine($"[SystemTools] {message}");
    }
}
