namespace SystemTools.Common.Layout;

public static class FloatingWindowRows
{
    public static List<List<string>> WithFallback(IEnumerable<List<string>>? configured, IEnumerable<string> available, ISet<string> explicitlyConfigured)
    {
        var validIds = available.Distinct().ToList();
        var valid = validIds.ToHashSet();
        var rows = (configured ?? []).Select(row => row.Where(valid.Contains).Distinct().ToList()).Where(row => row.Count > 0).ToList();
        var configuredIds = rows.SelectMany(row => row).ToHashSet();
        var missing = validIds.Where(id => !configuredIds.Contains(id) && !explicitlyConfigured.Contains(id)).ToList();
        if (missing.Count == 0) return rows;
        if (rows.Count == 0) rows.Add(missing); else rows[0].AddRange(missing);
        return rows;
    }
}
