using SystemTools.Contracts;
using System.Security.Cryptography;

namespace SystemTools.Platforms.MacOS;

public sealed class KeychainProtector : IStoredCredentialProtector
{
    public string Scheme => "keychain:v1:";
    public Capability Availability => OperatingSystem.IsMacOS() ? NativeBridge.Probe("credentials.probe") : new(CapabilityState.Unsupported, "需要 macOS 钥匙串。");
    public string Protect(string plaintext)
    {
        if (plaintext.Length > 32768) throw new ArgumentException("凭据过长。");
        var id = Guid.NewGuid().ToString("N");
        try { NativeBridge.Require(new { operation = "credentials.store", id, secret = plaintext }); }
        catch (NativeFeatureException) { throw new CryptographicException("未能写入 macOS 钥匙串，请解锁钥匙串或允许本应用访问；原配置保持不变。"); }
        return Scheme + id;
    }
    public bool TryUnprotect(string reference, out string plaintext)
    {
        plaintext = "";
        if (!TryId(reference, out var id) || !OperatingSystem.IsMacOS()) return false;
        try { plaintext = NativeBridge.Require(new { operation = "credentials.read", id }).GetProperty("secret").GetString()!; return true; }
        catch (Exception error) when (error is NativeFeatureException or DllNotFoundException or EntryPointNotFoundException) { return false; }
    }
    public bool Delete(string reference)
    {
        if (!TryId(reference, out var id)) return false;
        try { NativeBridge.Require(new { operation = "credentials.delete", id }); return true; }
        catch (NativeFeatureException) { return false; }
    }
    private bool TryId(string reference, out string id)
    {
        id = reference.StartsWith(Scheme, StringComparison.Ordinal) ? reference[Scheme.Length..] : "";
        return id.Length == 32 && Guid.TryParseExact(id, "N", out _);
    }
}
