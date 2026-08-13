using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace SystemTools.Settings;

public class WindowsHelloSettings : ObservableObject
{
    private bool _isConfigured;
    private bool _operating;
    private bool _hasError;
    private string _statusMessage = "正在检查 Windows Hello…";

    public bool IsConfigured
    {
        get => _isConfigured;
        set => SetProperty(ref _isConfigured, value);
    }

    [JsonIgnore]
    public bool Operating
    {
        get => _operating;
        set => SetProperty(ref _operating, value);
    }

    [JsonIgnore]
    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value);
    }

    [JsonIgnore]
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }
}
