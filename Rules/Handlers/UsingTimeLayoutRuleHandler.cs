using System;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Shared;
using SystemTools.Rules;

namespace SystemTools;

public partial class Plugin
{
    private static bool HandleUsingTimeLayoutRule(object? settings)
    {
        if (settings is not UsingTimeLayoutRuleSettings ruleSettings ||
            !Guid.TryParse(ruleSettings.TimeLayoutId, out var timeLayoutId))
        {
            return false;
        }

        var profile = IAppHost.TryGetService<IProfileService>()?.Profile;
        if (profile == null || !profile.TimeLayouts.TryGetValue(timeLayoutId, out var timeLayout))
        {
            return false;
        }

        return timeLayout.IsActivated;
    }
}
