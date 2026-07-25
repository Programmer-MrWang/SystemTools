using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SystemTools.Triggers;

public partial class KeywordTriggerConfig : ObservableRecipient
{
    [JsonIgnore]
    public DateTime LastTriggered { get; set; } = DateTime.MinValue;

    [ObservableProperty]
    private string _keyword = "";

    [ObservableProperty]
    private double _threshold = 0.5;
}
