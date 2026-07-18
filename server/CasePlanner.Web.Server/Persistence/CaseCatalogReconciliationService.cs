namespace CasePlanner.Web.Server.Persistence;

public sealed record CaseCatalogMismatch(long Id, string Field, string? SqliteValue, string? SqlServerValue);
public sealed record CaseCatalogReconciliation(
    bool Matches,
    int SqliteCount,
    int SqlServerCount,
    List<long> MissingFromSqlServer,
    List<long> MissingFromSqlite,
    List<CaseCatalogMismatch> Mismatches);

public sealed class CaseCatalogReconciliationService(
    SqliteCaseCatalogReader sqlite,
    SqlServerCaseCatalogReader sqlServer)
{
    public async Task<CaseCatalogReconciliation> CompareAsync(CancellationToken cancellationToken = default)
    {
        var query = new CaseCatalogQuery(IncludeClosed: true);
        var sqliteRows = await sqlite.GetCasesAsync(query, cancellationToken);
        var sqlRows = await sqlServer.GetCasesAsync(query, cancellationToken);
        var sqliteById = sqliteRows.ToDictionary(c => c.Id);
        var sqlById = sqlRows.ToDictionary(c => c.Id);
        var missingSql = sqliteById.Keys.Except(sqlById.Keys).Order().ToList();
        var missingSqlite = sqlById.Keys.Except(sqliteById.Keys).Order().ToList();
        var mismatches = new List<CaseCatalogMismatch>();

        foreach (var id in sqliteById.Keys.Intersect(sqlById.Keys).Order())
        {
            var left = sqliteById[id];
            var right = sqlById[id];
            Compare(id, nameof(left.CaseNumber), left.CaseNumber, right.CaseNumber, mismatches);
            Compare(id, nameof(left.CaseName), left.CaseName, right.CaseName, mismatches);
            Compare(id, nameof(left.JobNumber), left.JobNumber, right.JobNumber, mismatches);
            Compare(id, nameof(left.Tract), left.Tract, right.Tract, mismatches);
            Compare(id, nameof(left.County), left.County, right.County, mismatches);
            Compare(id, nameof(left.Status), left.Status, right.Status, mismatches);
            Compare(id, nameof(left.Stage), left.Stage, right.Stage, mismatches);
            Compare(id, nameof(left.Track), left.Track, right.Track, mismatches);
            Compare(id, nameof(left.CaseStatus), left.CaseStatus, right.CaseStatus, mismatches);
            Compare(id, nameof(left.ChecklistTotal), left.ChecklistTotal.ToString(), right.ChecklistTotal.ToString(), mismatches);
            Compare(id, nameof(left.ChecklistDone), left.ChecklistDone.ToString(), right.ChecklistDone.ToString(), mismatches);
        }

        return new(missingSql.Count == 0 && missingSqlite.Count == 0 && mismatches.Count == 0,
            sqliteRows.Count, sqlRows.Count, missingSql, missingSqlite, mismatches.Take(100).ToList());
    }

    private static void Compare(long id, string field, string? left, string? right, List<CaseCatalogMismatch> result)
    {
        if (!string.Equals(left, right, StringComparison.Ordinal)) result.Add(new(id, field, left, right));
    }
}
