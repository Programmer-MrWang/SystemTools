using Avalonia.Controls;
using Avalonia.Layout;
using ClassIsland.Core.Abstractions.Controls;
using System;
using SystemTools.Rules;

namespace SystemTools.Controls;


public class InTimePeriodRuleSettingsControl : RuleSettingsControlBase<InTimePeriodRuleSettings>
{
    private readonly TimePicker _startTimePicker;
    private readonly TimePicker _endTimePicker;

    public InTimePeriodRuleSettingsControl()
    {
        var panel = new StackPanel { Spacing = 10 };
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*,Auto"),
            ColumnSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        row.Children.Add(new TextBlock { Text = "从", VerticalAlignment = VerticalAlignment.Center });
        
        _startTimePicker = new TimePicker 
        { 
            ClockIdentifier = "24HourClock", 
            HorizontalAlignment = HorizontalAlignment.Center, 
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_startTimePicker, 1);
        row.Children.Add(_startTimePicker);

        var sep = new TextBlock { Text = "至", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(sep, 2);
        row.Children.Add(sep);

        _endTimePicker = new TimePicker 
        { 
            ClockIdentifier = "24HourClock", 
            HorizontalAlignment = HorizontalAlignment.Center, 
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_endTimePicker, 3);
        row.Children.Add(_endTimePicker);

        var hint = new TextBlock
        {
            Text = "提示：若起始晚于结束 将按跨天处理",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Foreground = Avalonia.Media.Brushes.Gray,
            FontSize = 12
        };
        panel.Children.Add(row);
        panel.Children.Add(hint);
        Content = panel;

        _startTimePicker.SelectedTimeChanged += (s, e) => SyncSettings();
        _endTimePicker.SelectedTimeChanged += (s, e) => SyncSettings();
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        
        if (TimeSpan.TryParse(Settings.StartTime, out var start))
        {
            _startTimePicker.SelectedTime = start;
        }
        
        if (TimeSpan.TryParse(Settings.EndTime, out var end))
        {
            _endTimePicker.SelectedTime = end;
        }
    }

    private void SyncSettings()
    {
        if (_startTimePicker.SelectedTime.HasValue)
        {
            Settings.StartTime = _startTimePicker.SelectedTime.Value.ToString(@"hh\:mm\:ss");
        }

        if (_endTimePicker.SelectedTime.HasValue)
        {
            Settings.EndTime = _endTimePicker.SelectedTime.Value.ToString(@"hh\:mm\:ss");
        }
    }
}
