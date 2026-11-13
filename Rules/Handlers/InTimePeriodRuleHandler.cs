namespace SystemTools;

public partial class Plugin
{
    private static bool HandleInTimePeriodRule(object? settings) => SharedRuleHandlers.HandleInTimePeriodRule(settings);
}
