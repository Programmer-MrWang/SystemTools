namespace SystemTools.Contracts;

public interface IStoredCredentialProtector : ICredentialProtector
{
    bool Delete(string reference);
}
public interface ICredentialBackend
{
    ICredentialProtector CreateCredentialProtector();
}
