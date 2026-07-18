namespace CasePlanner.Web.Server.Persistence;

public sealed record DiscoveryMismatch(long Id, string Field, string? SqliteValue, string? SqlServerValue);
public sealed record DiscoveryReconciliation(
    bool Matches,
    int SqliteCount,
    int SqlServerCount,
    List<long> MissingIds,
    List<DiscoveryMismatch> Mismatches);

public sealed class DiscoveryReconciliationService(
    SqliteDiscoveryTrackingStore sqlite,
    SqlServerDiscoveryTrackingStore sqlServer)
{
    public async Task<DiscoveryReconciliation> CompareAsync(CancellationToken token = default)
    {
        var source = await sqlite.GetAsync(null, token);
        var target = await sqlServer.GetAsync(null, token);
        var sourceById = source.ToDictionary(item => item.Id);
        var targetById = target.ToDictionary(item => item.Id);
        var mismatches = new List<DiscoveryMismatch>();

        foreach (var id in sourceById.Keys.Intersect(targetById.Keys))
        {
            var left = sourceById[id];
            var right = targetById[id];
            Compare(id, "CaseId", left.CaseId.ToString(), right.CaseId.ToString(), mismatches);
            Compare(id, "RequestTitle", left.RequestTitle, right.RequestTitle, mismatches);
            Compare(id, "Direction", left.Direction, right.Direction, mismatches);
            Compare(id, "DiscoveryType", left.DiscoveryType, right.DiscoveryType, mismatches);
            Compare(id, "ServedDate", left.ServedDate, right.ServedDate, mismatches);
            Compare(id, "DueDate", left.DueDate, right.DueDate, mismatches);
            Compare(id, "ResponseDate", left.ResponseDate, right.ResponseDate, mismatches);
            Compare(id, "FollowUpDate", left.FollowUpDate, right.FollowUpDate, mismatches);
            Compare(id, "Status", left.Status, right.Status, mismatches);
            Compare(id, "AssignedTo", left.AssignedTo, right.AssignedTo, mismatches);
        }

        var missingIds = sourceById.Keys.Except(targetById.Keys)
            .Concat(targetById.Keys.Except(sourceById.Keys))
            .Distinct()
            .Order()
            .ToList();
        return new(missingIds.Count == 0 && mismatches.Count == 0, source.Count, target.Count, missingIds, mismatches.Take(100).ToList());
    }

    private static void Compare(long id, string field, string? left, string? right, List<DiscoveryMismatch> result)
    {
        if (!string.Equals(left, right, StringComparison.Ordinal)) result.Add(new(id, field, left, right));
    }
}
