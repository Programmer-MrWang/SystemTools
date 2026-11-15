namespace SystemTools.Contracts;

public interface IPointerInput : INativeCapability
{
    string CaptureTarget();
    Task SendAsync(string operation, int x, int y, int value, string target, CancellationToken token);
}
public interface IDesktopSession
{
    Capability Probe(string operation);
    Task<string> ExecuteAsync(string operation, string? value, CancellationToken token);
}
