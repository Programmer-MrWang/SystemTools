using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ClassIsland.Core.Abstractions.Controls;
using SystemTools.Rules;

namespace SystemTools.Controls;

public partial class InTimePeriodRuleSettingsControl : RuleSettingsControlBase<InTimePeriodRuleSettings>
{
    public InTimePeriodRuleSettingsControl()
    {
        InitializeComponent();
    }

    private void StartTimePicker_OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is TimePicker picker && TimeSpan.TryParse(Settings.StartTime, out var start))
        {
            picker.SelectedTime = start;
        }
    }

    private void EndTimePicker_OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is TimePicker picker && TimeSpan.TryParse(Settings.EndTime, out var end))
        {
            picker.SelectedTime = end;
        }
    }

    private void StartTimePicker_OnSelectedTimeChanged(object? sender, TimePickerSelectedValueChangedEventArgs e)
    {
        if (sender is TimePicker { SelectedTime: { } selectedTime })
        {
            Settings.StartTime = selectedTime.ToString(@"hh\:mm\:ss");
        }
    }

    private void EndTimePicker_OnSelectedTimeChanged(object? sender, TimePickerSelectedValueChangedEventArgs e)
    {
        if (sender is TimePicker { SelectedTime: { } selectedTime })
        {
            Settings.EndTime = selectedTime.ToString(@"hh\:mm\:ss");
        }
    }
}
