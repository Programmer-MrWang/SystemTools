using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SystemTools.Triggers;

public partial class FloatingWindowTriggerConfig : ObservableRecipient
{
    [ObservableProperty] private string _buttonId = Guid.NewGuid().ToString("N");
    [ObservableProperty] private string _icon = "/uEA37";
    [ObservableProperty] private string _buttonName = "触发按钮 1";
}
