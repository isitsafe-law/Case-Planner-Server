namespace CasePlanner.Web.Server.Persistence;

public sealed record CaseWorkspaceMismatch(string Kind, long Id, string Field, string? SqliteValue, string? SqlServerValue);
public sealed record CaseWorkspaceReconciliation(
    bool Matches,
    int SqliteNotes,
    int SqlServerNotes,
    int SqliteHearings,
    int SqlServerHearings,
    List<long> MissingNoteIds,
    List<long> MissingHearingIds,
    List<CaseWorkspaceMismatch> Mismatches);

public sealed class CaseWorkspaceReconciliationService(
    SqliteCaseNoteStore sqliteNotes,
    SqlServerCaseNoteStore sqlNotes,
    SqliteHearingStore sqliteHearings,
    SqlServerHearingStore sqlHearings)
{
    public async Task<CaseWorkspaceReconciliation> CompareAsync(CancellationToken token = default)
    {
        var sn=await sqliteNotes.GetAsync(null,token); var tn=await sqlNotes.GetAsync(null,token);
        var sh=await sqliteHearings.GetAsync(null,token); var th=await sqlHearings.GetAsync(null,token);
        var snBy=sn.ToDictionary(x=>x.Id); var tnBy=tn.ToDictionary(x=>x.Id); var shBy=sh.ToDictionary(x=>x.Id); var thBy=th.ToDictionary(x=>x.Id);
        var mismatches=new List<CaseWorkspaceMismatch>();
        foreach(var id in snBy.Keys.Intersect(tnBy.Keys))
        {
            var a=snBy[id];var b=tnBy[id]; Compare("CaseNote",id,"CaseId",a.CaseId.ToString(),b.CaseId.ToString(),mismatches); Compare("CaseNote",id,"Title",a.Title,b.Title,mismatches); Compare("CaseNote",id,"Body",a.Body,b.Body,mismatches); Compare("CaseNote",id,"CreatedAt",a.CreatedAt,b.CreatedAt,mismatches); Compare("CaseNote",id,"UpdatedAt",a.UpdatedAt,b.UpdatedAt,mismatches);
        }
        foreach(var id in shBy.Keys.Intersect(thBy.Keys))
        {
            var a=shBy[id];var b=thBy[id]; Compare("Hearing",id,"CaseId",a.CaseId.ToString(),b.CaseId.ToString(),mismatches); Compare("Hearing",id,"Title",a.Title,b.Title,mismatches); Compare("Hearing",id,"HearingDate",a.HearingDate,b.HearingDate,mismatches); Compare("Hearing",id,"Location",a.Location,b.Location,mismatches); Compare("Hearing",id,"Description",a.Description,b.Description,mismatches); Compare("Hearing",id,"CreatedAt",a.CreatedAt,b.CreatedAt,mismatches); Compare("Hearing",id,"UpdatedAt",a.UpdatedAt,b.UpdatedAt,mismatches);
        }
        var missingNotes=Missing(snBy,tnBy); var missingHearings=Missing(shBy,thBy);
        return new(missingNotes.Count==0&&missingHearings.Count==0&&mismatches.Count==0,sn.Count,tn.Count,sh.Count,th.Count,missingNotes,missingHearings,mismatches.Take(100).ToList());
    }

    private static List<long> Missing<T>(Dictionary<long,T> left,Dictionary<long,T> right) => left.Keys.Except(right.Keys).Concat(right.Keys.Except(left.Keys)).Distinct().Order().ToList();
    private static void Compare(string kind,long id,string field,string? left,string? right,List<CaseWorkspaceMismatch> result){if(!string.Equals(left,right,StringComparison.Ordinal))result.Add(new(kind,id,field,left,right));}
}
