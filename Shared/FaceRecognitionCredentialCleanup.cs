using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Services.Management;
using ClassIsland.Shared;
using Microsoft.Extensions.Logging;

namespace SystemTools.Shared;

public static class FaceRecognitionCredentialCleanup
{
    private const string FaceRecognitionProviderId = "systemtools.authProviders.faceRecognition";
    private const string WindowsHelloProviderId = "systemtools.authProviders.windowsHello";

    public static bool RemoveFaceRecognitionProviderFromManagementCredentials(ILogger? logger = null)
        => RemoveProviderFromManagementCredentials(FaceRecognitionProviderId, "人脸识别", logger);

    public static bool RemoveWindowsHelloProviderFromManagementCredentials(ILogger? logger = null)
        => RemoveProviderFromManagementCredentials(WindowsHelloProviderId, "Windows Hello", logger);

    private static bool RemoveProviderFromManagementCredentials(
        string providerId,
        string providerName,
        ILogger? logger)
    {
        var changed = false;

        try
        {
            var managementService = IAppHost.TryGetService<IManagementService>();
            if (managementService != null)
            {
                changed |= TrySanitizeCredential(
                    managementService.CredentialConfig.UserCredential,
                    sanitized => managementService.CredentialConfig.UserCredential = sanitized,
                    providerId);
                changed |= TrySanitizeCredential(
                    managementService.CredentialConfig.AdminCredential,
                    sanitized => managementService.CredentialConfig.AdminCredential = sanitized,
                    providerId);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[SystemTools]清理当前 {ProviderName} 认证配置失败", providerName);
        }

        foreach (var credentialsPath in GetCredentialPaths())
        {
            if (!File.Exists(credentialsPath))
            {
                continue;
            }

            try
            {
                var root = JsonNode.Parse(File.ReadAllText(credentialsPath)) as JsonObject;
                if (root == null)
                {
                    continue;
                }

                var fileChanged = false;
                fileChanged |= TrySanitizeCredentialProperty(root, "UserCredential", providerId);
                fileChanged |= TrySanitizeCredentialProperty(root, "AdminCredential", providerId);

                if (!fileChanged)
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(credentialsPath)!);
                File.WriteAllText(credentialsPath, root.ToJsonString(new JsonSerializerOptions()));
                logger?.LogWarning("[SystemTools]已移除 {Path} 中依赖 {ProviderName} 验证器的认证项。", credentialsPath, providerName);
                changed = true;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "[SystemTools]清理 {ProviderName} 验证配置失败：{Path}", providerName, credentialsPath);
            }
        }

        return changed;
    }

    private static bool TrySanitizeCredential(string credentialString, Action<string> apply, string providerId)
    {
        if (!TryRemoveProvider(credentialString, providerId, out var sanitized) || sanitized == credentialString)
        {
            return false;
        }

        apply(sanitized);
        return true;
    }

    private static IEnumerable<string> GetCredentialPaths()
    {
        yield return Path.Combine(CommonDirectories.AppConfigPath, "Management", "Credentials.json");
        yield return Path.Combine(CommonDirectories.AppDataFolderPath, "Management", "Credentials.json");
    }

    private static bool TrySanitizeCredentialProperty(JsonObject root, string propertyName, string providerId)
    {
        var original = root[propertyName]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(original))
        {
            return false;
        }

        if (!TryRemoveProvider(original, providerId, out var sanitized) || sanitized == original)
        {
            return false;
        }

        root[propertyName] = sanitized;
        return true;
    }

    private static bool TryRemoveProvider(string credentialString, string providerId, out string sanitized)
    {
        sanitized = credentialString;

        try
        {
            var credentialJson = Encoding.UTF8.GetString(Convert.FromBase64String(credentialString));
            var credentialRoot = JsonNode.Parse(credentialJson) as JsonObject;
            var items = credentialRoot?["Items"] as JsonArray;
            if (credentialRoot == null || items == null)
            {
                return false;
            }

            var keptItems = items
                .OfType<JsonObject>()
                .Where(item => !string.Equals(item["ProviderId"]?.GetValue<string>(), providerId,
                    StringComparison.Ordinal))
                .ToArray();

            if (keptItems.Length == items.Count)
            {
                return false;
            }

            if (keptItems.Length == 0)
            {
                sanitized = string.Empty;
                return true;
            }

            credentialRoot["Items"] = new JsonArray(keptItems.Select(item => item.DeepClone()).ToArray());
            sanitized = Convert.ToBase64String(Encoding.UTF8.GetBytes(credentialRoot.ToJsonString(new JsonSerializerOptions())));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
