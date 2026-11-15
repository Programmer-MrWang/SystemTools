using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using SystemTools.Triggers;

namespace SystemTools;

[TriggerInfo("SystemTools.KeywordTrigger", "关键词触发", "\uED53")]
public sealed class NativeKeywordTrigger(PortableSpeechService speech) : TriggerBase<KeywordTriggerConfig>
{
    private IDisposable? _registration;
    public override void Loaded() { Settings.PropertyChanged += Changed; Register(); }
    public override void UnLoaded() { Settings.PropertyChanged -= Changed; _registration?.Dispose(); _registration = null; }
    private void Changed(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName is nameof(Settings.Keyword) or nameof(Settings.Threshold)) Register(); }
    private void Register()
    {
        _registration?.Dispose(); _registration = null;
        if (!string.IsNullOrWhiteSpace(Settings.Keyword))
            _registration = speech.Input.RegisterKeyword(Settings.Keyword, Settings.Threshold, () => Dispatcher.UIThread.Post(Trigger));
    }
}

public sealed class NativeKeywordSettingsControl : TriggerSettingsControlBase<KeywordTriggerConfig>
{
    public NativeKeywordSettingsControl()
    {
        var panel = new StackPanel { Spacing = 8 };
        var keyword = new TextBox { PlaceholderText = "要识别的关键词" };
        keyword.Bind(TextBox.TextProperty, new Binding("Settings.Keyword") { Source = this, Mode = BindingMode.TwoWay });
        var threshold = new NumericUpDown { Minimum = 0, Maximum = 1, Increment = 0.1m };
        threshold.Bind(NumericUpDown.ValueProperty, new Binding("Settings.Threshold") { Source = this, Mode = BindingMode.TwoWay });
        panel.Children.Add(keyword); panel.Children.Add(threshold); Content = panel;
    }
}
