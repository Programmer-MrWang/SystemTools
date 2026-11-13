namespace SystemTools;

public partial class Plugin
{
    private static bool HandleProcessRunningRule(object? settings) => SharedRuleHandlers.HandleProcessRunningRule(settings);
}
