using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SystemTools.Triggers;

public class ActionInProgressTriggerConfig : ObservableRecipient
{
    private string _triggerId = string.Empty;

    public string TriggerId
    {
        get => _triggerId;
        set
        {
            if (_triggerId == value) return;
            _triggerId = value;
            OnPropertyChanged();
        }
    }
}