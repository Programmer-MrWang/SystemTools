using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SystemTools.Triggers;

public class HotkeyTriggerConfig : ObservableRecipient
{
    [JsonIgnore]
    public DateTime LastTriggered { get; set; } = DateTime.MinValue;
}