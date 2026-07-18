using System.Globalization;
using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

public sealed record ActivityDocumentMismatch(string Kind,long Id,string Field,string? SqliteValue,string? SqlServerValue);
public sealed record ActivityDocumentReconciliation(bool Matches,int SqliteActivities,int SqlServerActivities,
    int SqliteDocuments,int SqlServerDocuments,List<long> MissingActivityIds,List<long> MissingDocumentIds,
    List<ActivityDocumentMismatch> Mismatches);

public sealed class ActivityDocumentReconciliationService(
    CasePlannerRepository sqlite,SqlServerActivityStore sqlActivities,SqlServerDocumentExportStore sqlDocuments)
{
    public async Task<ActivityDocumentReconciliation> CompareAsync(CancellationToken token=default)
    {
        var sa=await sqlite.GetActivityLogAsync(null);var ta=await sqlActivities.GetAsync(null,token);
        var sd=await sqlite.GetDocumentExportsAsync(null);var td=await sqlDocuments.GetAsync(null,token);
        var a=sa.ToDictionary(x=>x.Id);var b=ta.ToDictionary(x=>x.Id);var d=sd.ToDictionary(x=>x.Id);var e=td.ToDictionary(x=>x.Id);
        var mismatches=new List<ActivityDocumentMismatch>();
        foreach(var id in a.Keys.Intersect(b.Keys)){var x=a[id];var y=b[id];C("Activity",id,"CaseId",V(x.CaseId),V(y.CaseId),mismatches);C("Activity",id,"ActivityType",x.ActivityType,y.ActivityType,mismatches);C("Activity",id,"IsMeaningful",V(x.IsMeaningful),V(y.IsMeaningful),mismatches);C("Activity",id,"OccurredAt",x.OccurredAt,y.OccurredAt,mismatches);C("Activity",id,"Notes",x.Notes,y.Notes,mismatches);C("Activity",id,"CreatedAt",x.CreatedAt,y.CreatedAt,mismatches);C("Activity",id,"ActorUserId",x.ActorUserId,y.ActorUserId,mismatches);C("Activity",id,"ActorDisplay",x.ActorDisplay,y.ActorDisplay,mismatches);C("Activity",id,"HistoryCount",V(x.History.Count),V(y.History.Count),mismatches);}
        foreach(var id in d.Keys.Intersect(e.Keys)){var x=d[id];var y=e[id];C("Document",id,"CaseId",V(x.CaseId),V(y.CaseId),mismatches);C("Document",id,"DocumentType",x.DocumentType,y.DocumentType,mismatches);C("Document",id,"DocumentTitle",x.DocumentTitle,y.DocumentTitle,mismatches);C("Document",id,"OutputPath",x.OutputPath,y.OutputPath,mismatches);C("Document",id,"CreatedAt",x.CreatedAt,y.CreatedAt,mismatches);C("Document",id,"Status",x.Status,y.Status,mismatches);C("Document",id,"QaStatus",x.QaStatus,y.QaStatus,mismatches);C("Document",id,"QaNotes",x.QaNotes,y.QaNotes,mismatches);C("Document",id,"CreatedByUserId",x.CreatedByUserId,y.CreatedByUserId,mismatches);C("Document",id,"CreatedByDisplay",x.CreatedByDisplay,y.CreatedByDisplay,mismatches);C("Document",id,"QaReviewedByUserId",x.QaReviewedByUserId,y.QaReviewedByUserId,mismatches);C("Document",id,"QaReviewedByDisplay",x.QaReviewedByDisplay,y.QaReviewedByDisplay,mismatches);}
        var ma=M(a,b);var md=M(d,e);return new(ma.Count==0&&md.Count==0&&mismatches.Count==0,sa.Count,ta.Count,sd.Count,td.Count,ma,md,mismatches.Take(100).ToList());
    }
    private static string? V(object? value)=>value is null?null:Convert.ToString(value,CultureInfo.InvariantCulture);
    private static List<long>M<T>(Dictionary<long,T>a,Dictionary<long,T>b)=>a.Keys.Except(b.Keys).Concat(b.Keys.Except(a.Keys)).Distinct().Order().ToList();
    private static void C(string kind,long id,string field,string? a,string? b,List<ActivityDocumentMismatch> result){if(!string.Equals(a,b,StringComparison.Ordinal))result.Add(new(kind,id,field,a,b));}
}
