using System;
using System.Security.Cryptography;
using System.Text;
using SystemTools.Contracts;

namespace SystemTools.Platforms.Windows;

public sealed class WindowsDpapiProtector : ICredentialProtector
{
    public string Scheme => "dpapi-current-user:v1:";
    public Capability Availability => OperatingSystem.IsWindows()
        ? new(CapabilityState.Available) : new(CapabilityState.Unsupported, "DPAPI 仅在 Windows 可用。" );
    public string Protect(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        try { return Scheme + Convert.ToBase64String(ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public bool TryUnprotect(string ciphertext, out string plaintext)
    {
        plaintext = "";
        if (!ciphertext.StartsWith(Scheme, StringComparison.Ordinal) || !Availability.CanExecute) return false;
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(ciphertext[Scheme.Length..]), null, DataProtectionScope.CurrentUser);
            try { plaintext = Encoding.UTF8.GetString(bytes); return true; }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { return false; }
    }
}
