namespace SystemTools.Contracts;

public interface ICredentialProtector
{
    string Scheme { get; }
    Capability Availability { get; }
    string Protect(string plaintext);
    bool TryUnprotect(string ciphertext, out string plaintext);
}

public sealed class UnavailableCredentialProtector(string scheme, string reason) : ICredentialProtector
{
    public string Scheme => scheme;
    public Capability Availability => new(CapabilityState.Unsupported, reason);
    public string Protect(string plaintext) => throw new PlatformNotSupportedException(reason);
    public bool TryUnprotect(string ciphertext, out string plaintext) { plaintext = ""; return false; }
}
