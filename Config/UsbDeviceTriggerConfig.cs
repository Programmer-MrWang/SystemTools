using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SystemTools.Triggers;

public class UsbDeviceTriggerConfig : ObservableRecipient
{
    [JsonIgnore]
    public DateTime LastTriggered { get; set; } = DateTime.Now;
}