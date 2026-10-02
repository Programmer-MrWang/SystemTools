using System;
using System.Security.Cryptography;
using System.Text;

namespace SystemTools.Shared;

internal static class CredentialProtection
{
    private const string Prefix = "dpapi-current-user:v1:";

    internal static bool IsProtected(string value) =>
        value.StartsWith(Prefix, StringComparison.Ordinal);

    internal static string Protect(string value)
    {
        if (value.Length == 0)
            return string.Empty;

        var bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            return Prefix + Convert.ToBase64String(
                ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    internal static bool TryUnprotect(string value, out string result)
    {
        result = string.Empty;
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(value[Prefix.Length..]),
                null, DataProtectionScope.CurrentUser);
            try
            {
                result = Encoding.UTF8.GetString(bytes);
                return true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // A copied profile may belong to a different Windows account. The
            // caller retains its encrypted value until the user replaces the key.
            return false;
        }
    }
}
