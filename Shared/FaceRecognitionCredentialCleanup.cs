using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClassIsland.Core;
using Microsoft.Extensions.Logging;

namespace SystemTools.Shared;

public static class FaceRecognitionCredentialCleanup
{
    private const string FaceRecognitionProviderId = "systemtools.authProviders.faceRecognition";

    public static bool RemoveFaceRecognitionProviderFromManagementCredentials(ILogger? logger = null)
    {
        var changed = false;

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
                fileChanged |= TrySanitizeCredentialProperty(root, "UserCredential");
                fileChanged |= TrySanitizeCredentialProperty(root, "AdminCredential");

                if (!fileChanged)
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(credentialsPath)!);
                File.WriteAllText(credentialsPath, root.ToJsonString(new JsonSerializerOptions()));
                logger?.LogWarning("[SystemTools]已移除 {Path} 中依赖人脸识别验证器的认证项。", credentialsPath);
                changed = true;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "[SystemTools]清理人脸识别验证配置失败：{Path}", credentialsPath);
            }
        }

        return changed;
    }

    private static IEnumerable<string> GetCredentialPaths()
    {
        yield return Path.Combine(CommonDirectories.AppConfigPath, "Management", "Credentials.json");
        yield return Path.Combine(CommonDirectories.AppDataFolderPath, "Management", "Credentials.json");
    }

    private static bool TrySanitizeCredentialProperty(JsonObject root, string propertyName)
    {
        var original = root[propertyName]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(original))
        {
            return false;
        }

        if (!TryRemoveFaceRecognitionProvider(original, out var sanitized) || sanitized == original)
        {
            return false;
        }

        root[propertyName] = sanitized;
        return true;
    }

    private static bool TryRemoveFaceRecognitionProvider(string credentialString, out string sanitized)
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
                .Where(item => !string.Equals(item["ProviderId"]?.GetValue<string>(), FaceRecognitionProviderId,
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
