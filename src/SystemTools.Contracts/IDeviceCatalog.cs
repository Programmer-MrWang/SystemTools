namespace SystemTools.Contracts;

public sealed record DeviceOption(string Name, string Id);
public interface IDeviceCatalog
{
    IReadOnlyList<DeviceOption> GetDevices();
}
