using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SystemTools.Triggers;

public class UsbDeviceTriggerConfig : ObservableRecipient
{
    private bool _onlyUsbStorage = true;
    private DateTime _lastTriggered = DateTime.Now;

    [JsonPropertyName("onlyUsbStorage")]
    public bool OnlyUsbStorage
    {
        get => _onlyUsbStorage;
        set => SetProperty(ref _onlyUsbStorage, value);
    }

    [JsonIgnore]
    public DateTime LastTriggered
    {
        get => _lastTriggered;
        set => SetProperty(ref _lastTriggered, value);
    }
}