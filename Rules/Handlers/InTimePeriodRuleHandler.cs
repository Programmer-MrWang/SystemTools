using System;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Shared;
using SystemTools.Rules;

namespace SystemTools;

public partial class Plugin
{
    private static bool HandleInTimePeriodRule(object? settings)
    {
        if (settings is not InTimePeriodRuleSettings ruleSettings ||
            !TimeSpan.TryParse(ruleSettings.StartTime, out var start) ||
            !TimeSpan.TryParse(ruleSettings.EndTime, out var end))
        {
            return false;
        }

        var current = IAppHost.TryGetService<IExactTimeService>()?.GetCurrentLocalDateTime().TimeOfDay ?? DateTime.Now.TimeOfDay;
        if (start <= end)
        {
            return current >= start && current <= end;
        }

        return current >= start || current <= end;
    }
}
