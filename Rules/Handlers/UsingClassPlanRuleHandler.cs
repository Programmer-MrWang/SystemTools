using System;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Shared;
using SystemTools.Rules;

namespace SystemTools;

public partial class Plugin
{
    private static bool HandleUsingClassPlanRule(object? settings)
    {
        if (settings is not UsingClassPlanRuleSettings ruleSettings ||
            !Guid.TryParse(ruleSettings.ClassPlanId, out var classPlanId))
        {
            return false;
        }

        var profile = IAppHost.TryGetService<IProfileService>()?.Profile;
        if (profile == null || !profile.ClassPlans.TryGetValue(classPlanId, out var classPlan))
        {
            return false;
        }

        return classPlan.IsActivated;
    }
}
