using Avalonia.Controls;
using Avalonia.Data;
using ClassIsland.Core.Abstractions.Controls;
using SystemTools.Triggers;

namespace SystemTools;

public sealed class NativeIdleSettingsControl : TriggerSettingsControlBase<LongIdleTriggerConfig>
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        var value = new NumericUpDown { Minimum = 1, Maximum = 10080, Increment = 1 };
        value.Bind(NumericUpDown.ValueProperty, new Binding(nameof(Settings.IdleMinutes)) { Source = Settings, Mode = BindingMode.TwoWay });
        Content = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = "连续未操作多少分钟后触发" }, value } };
    }
}
