namespace CasePlanner.Web.Server.Persistence;

public sealed record ReferenceLibraryReconciliation(bool Matches, int LocalCount, int SqlServerCount, List<string> Mismatches);

public sealed class ReferenceLibraryReconciliationService(FileReferenceLibraryStore local, SqlServerReferenceLibraryStore sql)
{
    public async Task<ReferenceLibraryReconciliation> CompareAsync(CancellationToken token = default)
    {
        var localItems = await local.GetAsync(token);
        var sqlItems = await sql.GetAsync(token);
        var localByKey = localItems.ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);
        var sqlByKey = sqlItems.ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);
        var mismatches = new List<string>();
        foreach (var key in localByKey.Keys.Union(sqlByKey.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
        {
            if (!localByKey.TryGetValue(key, out var left)) { mismatches.Add($"Missing locally: {key}"); continue; }
            if (!sqlByKey.TryGetValue(key, out var right)) { mismatches.Add($"Missing in SQL Server: {key}"); continue; }
            if (!string.Equals(left.Title, right.Title, StringComparison.Ordinal) ||
                !string.Equals(left.Description, right.Description, StringComparison.Ordinal) ||
                !string.Equals(left.Text, right.Text, StringComparison.Ordinal))
                mismatches.Add($"Content mismatch: {key}");
        }
        return new(mismatches.Count == 0, localItems.Count, sqlItems.Count, mismatches);
    }
}
