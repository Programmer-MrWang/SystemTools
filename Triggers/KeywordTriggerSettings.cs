using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassIsland.Core.Abstractions.Controls;

namespace SystemTools.Triggers;

public class KeywordTriggerSettings : TriggerSettingsControlBase<KeywordTriggerConfig>
{
    private TextBox? _keywordBox;
    private TextBlock? _thresholdLabel;
    private Slider? _thresholdSlider;

    public KeywordTriggerSettings() { BuildUI(); }

    private void BuildUI()
    {
        var panel = new StackPanel { Spacing = 12, Margin = new(10) };
        var keywordHeader = new TextBlock
        {
            Text = "关键词",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 4, 0, 0)
        };
        _keywordBox = new TextBox
        {
            PlaceholderText = "输入要检测的关键词…",
            FontSize = 14,
            Padding = new Thickness(10, 8),
            CornerRadius = new CornerRadius(6)
        };
        _keywordBox.PropertyChanged += (s, e) =>
        {
            if (e.Property.Name == nameof(TextBox.Text) && Settings != null)
                Settings.Keyword = _keywordBox.Text ?? "";
        };
        var thresholdHeader = new TextBlock
        {
            Text = "识别灵敏度",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 6, 0, 0)
        };
        var thresholdRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,60"),
            Margin = new Thickness(0, -3, 0, 0)
        };
        _thresholdSlider = new Slider
        {
            Minimum = 0.0,
            Maximum = 1.0,
            TickFrequency = 0.05,
            IsSnapToTickEnabled = true,
            Value = 0.5,
            VerticalAlignment = VerticalAlignment.Center
        };
        _thresholdLabel = new TextBlock
        {
            Text = "0.50",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.Parse("#666666")),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _thresholdSlider.PropertyChanged += (s, e) =>
        {
            if (e.Property.Name == nameof(Slider.Value))
            {
                var val = Math.Round(_thresholdSlider.Value, 2);
                _thresholdLabel!.Text = val.ToString("F2");
                if (Settings != null) Settings.Threshold = val;
            }
        };
        thresholdRow.Children.Add(_thresholdSlider);
        Grid.SetColumn(_thresholdSlider, 0);
        thresholdRow.Children.Add(_thresholdLabel);
        Grid.SetColumn(_thresholdLabel, 1);
        var tipText = new TextBlock
        {
            Text = "阈值越低越灵敏，越高越严格 建议 < 0.3",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#888888")),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, -3, 0, 6)
        };
        var noteText = new TextBlock
        {
            Text = "需要 Windows 中文语音识别支持\n控制面板 → 语音识别 → 安装中文语音 \n或 设置 → 隐私和安全性 → 语音 → 在线语音识别",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#996600")),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, -1, 0, 0)
        };
        panel.Children.Add(keywordHeader);
        panel.Children.Add(_keywordBox);
        panel.Children.Add(thresholdHeader);
        panel.Children.Add(thresholdRow);
        panel.Children.Add(tipText);
        panel.Children.Add(noteText);
        Content = panel;
    }

    protected override void OnAttachedToLogicalTree(Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        LoadSettings();
    }

    private void LoadSettings()
    {
        if (Settings == null) return;
        if (_keywordBox != null) _keywordBox.Text = Settings.Keyword;
        if (_thresholdSlider != null) _thresholdSlider.Value = Settings.Threshold;
    }
}
