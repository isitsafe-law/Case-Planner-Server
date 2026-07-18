namespace CasePlanner.Web.Server.Persistence;

public sealed record WorkItemMismatch(string Kind, long Id, string Field, string? SqliteValue, string? SqlServerValue);
public sealed record WorkItemReconciliation(bool Matches, int SqliteDeadlines, int SqlServerDeadlines, int SqliteChecklist, int SqlServerChecklist, List<long> MissingDeadlineIds, List<long> MissingChecklistIds, List<WorkItemMismatch> Mismatches);

public sealed class WorkItemReconciliationService(SqliteDeadlineStore sqliteDeadlines, SqlServerDeadlineStore sqlDeadlines, SqliteChecklistStore sqliteChecklist, SqlServerChecklistStore sqlChecklist)
{
    public async Task<WorkItemReconciliation> CompareAsync(CancellationToken token = default)
    {
        var sd=await sqliteDeadlines.GetAsync(null,token);var td=await sqlDeadlines.GetAsync(null,token);var sc=await sqliteChecklist.GetAsync(null,token);var tc=await sqlChecklist.GetAsync(null,token);
        var sdBy=sd.ToDictionary(x=>x.Id);var tdBy=td.ToDictionary(x=>x.Id);var scBy=sc.ToDictionary(x=>x.Id);var tcBy=tc.ToDictionary(x=>x.Id);var mismatches=new List<WorkItemMismatch>();
        foreach(var id in sdBy.Keys.Intersect(tdBy.Keys)){var a=sdBy[id];var b=tdBy[id];Compare("Deadline",id,"CaseId",a.CaseId.ToString(),b.CaseId.ToString(),mismatches);Compare("Deadline",id,"Title",a.Title,b.Title,mismatches);Compare("Deadline",id,"DueDate",a.DueDate,b.DueDate,mismatches);Compare("Deadline",id,"Status",a.Status,b.Status,mismatches);Compare("Deadline",id,"Severity",a.Severity,b.Severity,mismatches);Compare("Deadline",id,"HistoryCount",a.History.Count.ToString(),b.History.Count.ToString(),mismatches);}
        foreach(var id in scBy.Keys.Intersect(tcBy.Keys)){var a=scBy[id];var b=tcBy[id];Compare("Checklist",id,"CaseId",a.CaseId.ToString(),b.CaseId.ToString(),mismatches);Compare("Checklist",id,"Phase",a.Phase,b.Phase,mismatches);Compare("Checklist",id,"Task",a.Task,b.Task,mismatches);Compare("Checklist",id,"DueDate",a.DueDate,b.DueDate,mismatches);Compare("Checklist",id,"Status",a.Status,b.Status,mismatches);}
        var missingD=sdBy.Keys.Except(tdBy.Keys).Concat(tdBy.Keys.Except(sdBy.Keys)).Distinct().Order().ToList();var missingC=scBy.Keys.Except(tcBy.Keys).Concat(tcBy.Keys.Except(scBy.Keys)).Distinct().Order().ToList();
        return new(missingD.Count==0&&missingC.Count==0&&mismatches.Count==0,sd.Count,td.Count,sc.Count,tc.Count,missingD,missingC,mismatches.Take(100).ToList());
    }
    private static void Compare(string kind,long id,string field,string? left,string? right,List<WorkItemMismatch> result){if(!string.Equals(left,right,StringComparison.Ordinal))result.Add(new(kind,id,field,left,right));}
}
