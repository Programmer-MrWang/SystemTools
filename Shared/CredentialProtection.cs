using System;
using SystemTools.Contracts;

namespace SystemTools.Shared;

internal static class CredentialProtection
{
    internal static ICredentialProtector Provider { get; set; } = new UnavailableCredentialProtector("unavailable", "本平台尚未接入安全凭据存储，请在支持的平台重新配置。");
    internal static bool IsProtected(string value) => value.StartsWith("dpapi-current-user:v1:", StringComparison.Ordinal) ||
        value.StartsWith("keychain:v1:", StringComparison.Ordinal) || value.StartsWith("secret-service:v1:", StringComparison.Ordinal);
    internal static string Protect(string value) => value.Length == 0 ? "" : Provider.Protect(value);
    internal static bool TryUnprotect(string value, out string result) => Provider.TryUnprotect(value, out result);
}
