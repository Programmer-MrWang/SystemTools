using System;
using System.Collections.Generic;
using System.Linq;

namespace SystemTools.Common.Automation;

public static class WorkflowTargetResolver
{
    public static T? Resolve<T>(IReadOnlyList<T> workflows, Guid? id, string name, int legacyIndex,
        Func<T, Guid> getId, Func<T, string> getName) where T : class
    {
        if (id is { } identity && identity != Guid.Empty)
            return workflows.FirstOrDefault(workflow => getId(workflow) == identity);
        if (!string.IsNullOrEmpty(name))
        {
            var matches = workflows.Where(workflow => getName(workflow) == name).Take(2).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
        return legacyIndex >= 0 && legacyIndex < workflows.Count ? workflows[legacyIndex] : null;
    }
}
