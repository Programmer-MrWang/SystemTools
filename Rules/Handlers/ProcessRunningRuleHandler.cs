using System;
using SystemTools.Rules;

namespace SystemTools;

public partial class Plugin
{
    private static bool HandleProcessRunningRule(object? settings)
    {
        if (settings is not ProcessRunningRuleSettings ruleSettings ||
            string.IsNullOrWhiteSpace(ruleSettings.ProcessName))
        {
            return false;
        }

        var processName = ruleSettings.ProcessName.Trim();
        if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            processName = processName[..^4];
        }

        try
        {
            return System.Diagnostics.Process.GetProcessesByName(processName).Length > 0;
        }
        catch
        {
            return false;
        }
    }
}
