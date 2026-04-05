using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ClassIsland.Core.Abstractions.Controls;
using SystemTools.Rules;

namespace SystemTools.Controls;

public partial class InTimePeriodRuleSettingsControl : RuleSettingsControlBase<InTimePeriodRuleSettings>
{
    public InTimePeriodRuleSettingsControl()
    {
        InitializeComponent();

        StartTimePicker.SelectedTimeChanged += (_, _) => SyncSettings();
        EndTimePicker.SelectedTimeChanged += (_, _) => SyncSettings();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        if (TimeSpan.TryParse(Settings.StartTime, out var start))
        {
            StartTimePicker.SelectedTime = start;
        }

        if (TimeSpan.TryParse(Settings.EndTime, out var end))
        {
            EndTimePicker.SelectedTime = end;
        }
    }

    private void SyncSettings()
    {
        if (StartTimePicker.SelectedTime.HasValue)
        {
            Settings.StartTime = StartTimePicker.SelectedTime.Value.ToString(@"hh\:mm\:ss");
        }

        if (EndTimePicker.SelectedTime.HasValue)
        {
            Settings.EndTime = EndTimePicker.SelectedTime.Value.ToString(@"hh\:mm\:ss");
        }
    }
}
