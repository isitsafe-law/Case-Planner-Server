namespace CasePlanner.Web.Server.Persistence;

public sealed record PublicationSummaryMismatch(long CaseId,string Field,string? SqliteValue,string? SqlServerValue);
public sealed record PublicationSummaryReconciliation(
    bool Matches,int SqliteCount,int SqlServerCount,List<long> MissingSqlServerCaseIds,
    List<long> ExtraSqlServerCaseIds,List<PublicationSummaryMismatch> Mismatches);

public sealed class PublicationSummaryReconciliationService(
    SqlitePublicationSummaryStore sqlite,
    SqlServerPublicationSummaryStore sqlServer)
{
    public async Task<PublicationSummaryReconciliation> CompareAsync(CancellationToken token=default)
    {
        var source=(await sqlite.GetAsync(null,token)).ToDictionary(x=>x.CaseId);
        var target=(await sqlServer.GetAsync(null,token)).ToDictionary(x=>x.CaseId);
        var mismatches=new List<PublicationSummaryMismatch>();
        foreach(var caseId in source.Keys.Intersect(target.Keys))
        {
            var a=source[caseId];var b=target[caseId];
            Compare(caseId,"FirstPublicationDate",a.FirstPublicationDate,b.FirstPublicationDate,mismatches);
            Compare(caseId,"SecondPublicationDate",a.SecondPublicationDate,b.SecondPublicationDate,mismatches);
            Compare(caseId,"PublicationName",a.PublicationName,b.PublicationName,mismatches);
            Compare(caseId,"MarkedPerfected",a.MarkedPerfected.ToString(),b.MarkedPerfected.ToString(),mismatches);
            Compare(caseId,"LastUpdatedAt",a.LastUpdatedAt,b.LastUpdatedAt,mismatches);
            Compare(caseId,"LastUpdatedBy",a.LastUpdatedBy,b.LastUpdatedBy,mismatches);
        }
        var missing=source.Keys.Except(target.Keys).Order().ToList();
        var extra=target.Keys.Except(source.Keys).Order().ToList();
        return new(missing.Count==0&&extra.Count==0&&mismatches.Count==0,source.Count,target.Count,missing,extra,mismatches);
    }

    private static void Compare(long caseId,string field,string? a,string? b,List<PublicationSummaryMismatch> result)
    {
        if(!string.Equals(a,b,StringComparison.Ordinal))result.Add(new(caseId,field,a,b));
    }
}
