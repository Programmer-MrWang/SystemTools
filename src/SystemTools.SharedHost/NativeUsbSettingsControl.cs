using Avalonia.Controls;
using Avalonia.Data;
using ClassIsland.Core.Abstractions.Controls;
using SystemTools.Triggers;

namespace SystemTools;

public sealed class NativeUsbSettingsControl : TriggerSettingsControlBase<UsbDeviceTriggerConfig>
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        var onlyStorage = new CheckBox { Content = "只监听已挂载的 USB 存储设备" };
        onlyStorage.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(Settings.OnlyUsbStorage)) { Source = Settings, Mode = BindingMode.TwoWay });
        Content = onlyStorage;
    }
}
