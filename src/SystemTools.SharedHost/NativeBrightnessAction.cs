using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using SystemTools.Contracts;

namespace SystemTools;

public sealed class NativeBrightnessSettings
{
    [System.Text.Json.Serialization.JsonPropertyName("brightnessPercent")] public int BrightnessPercent { get; set; } = 50;
    [System.Text.Json.Serialization.JsonPropertyName("notifyOnExecute")] public bool NotifyOnExecute { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("deviceId")] public string DeviceId { get; set; } = "";
}
[ActionInfo("SystemTools.AdjustScreenBrightness", "调整屏幕亮度", "\uF464", false)]
public sealed class NativeBrightnessAction(NativeDesktopServices native, FeatureCatalog catalog) : ActionBase<NativeBrightnessSettings>
{
    protected override async Task OnInvoke()
    {
        catalog.EnsureExecutable(ActionItem.Id);
        if (string.IsNullOrWhiteSpace(Settings.DeviceId)) throw new InvalidOperationException("请先选择需要调整硬件亮度的显示器。");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(InterruptCancellationToken, native.Stopping);
        var result = await native.Advanced.Brightness!.SetAsync(Settings.DeviceId, Settings.BrightnessPercent, stop.Token);
        NativeActionFeedback.Notify(Settings.NotifyOnExecute, $"{result.Name} 硬件亮度已调整为 {result.Percent:F0}%");
    }
}
public sealed class NativeBrightnessSettingsControl(NativeDesktopServices native) : ActionSettingsControlBase<NativeBrightnessSettings>
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        var devices = new ComboBox { PlaceholderText = "选择硬件亮度设备", MinWidth = 280,
            ItemTemplate = new FuncDataTemplate<BrightnessDevice>((item, _) => new TextBlock { Text = item is null ? "" : $"{item.Name} · {item.Kind} · {item.Percent:F0}%" }) };
        var percent = new NumericUpDown { Minimum = 0, Maximum = 100, Value = Settings.BrightnessPercent };
        percent.ValueChanged += (_, _) => Settings.BrightnessPercent = (int)(percent.Value ?? 50);
        devices.SelectionChanged += (_, _) => { if (devices.SelectedItem is BrightnessDevice selected) Settings.DeviceId = selected.Id; };
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var refresh = new Button { Content = "重新检测亮度设备" };
        async Task Refresh()
        {
            refresh.IsEnabled = false;
            try
            {
                var rows = await Task.Run(async () => native.Advanced.Brightness is { } brightness ? await brightness.ListAsync(native.Stopping) : []);
                devices.ItemsSource = rows; devices.SelectedItem = rows.SingleOrDefault(x => x.Id == Settings.DeviceId);
                status.Text = rows.Count == 0 ? "没有可控制的硬件背光/DDC 设备。请检查驱动、权限及外屏连接。" : "仅调整所选设备的硬件亮度。";
            }
            catch (Exception error) { status.Text = error.Message; }
            finally { refresh.IsEnabled = true; }
        }
        refresh.Click += async (_, _) => await Refresh();
        AttachedToVisualTree += async (_, _) => await Refresh();
        Content = new StackPanel { Spacing = 10, Children = { devices, percent, refresh, status } };
    }
}
