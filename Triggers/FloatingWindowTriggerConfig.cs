using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SystemTools.Triggers;

public partial class FloatingWindowTriggerConfig : ObservableRecipient
{
    [ObservableProperty] private string _buttonId = Guid.NewGuid().ToString("N");
    [ObservableProperty] private string _icon = "/uE7C3";
}
