namespace CasePlanner.Web.Server.Persistence;

public sealed record LitigationMismatch(string Kind,long Id,string Field,string? SqliteValue,string? SqlServerValue);
public sealed record LitigationReconciliation(bool Matches,int SqliteWitnesses,int SqlServerWitnesses,int SqliteExhibits,int SqlServerExhibits,int SqliteMotions,int SqlServerMotions,List<long> MissingWitnessIds,List<long> MissingExhibitIds,List<long> MissingMotionIds,List<LitigationMismatch> Mismatches);

public sealed class LitigationReconciliationService(SqliteWitnessStore sw,SqlServerWitnessStore tw,SqliteExhibitStore se,SqlServerExhibitStore te,SqliteTrialMotionStore sm,SqlServerTrialMotionStore tm)
{
    public async Task<LitigationReconciliation> CompareAsync(CancellationToken token=default)
    {
        var aW=await sw.GetAsync(null,token);var bW=await tw.GetAsync(null,token);var aE=await se.GetAsync(null,token);var bE=await te.GetAsync(null,token);var aM=await sm.GetAsync(null,token);var bM=await tm.GetAsync(null,token);
        var aw=aW.ToDictionary(x=>x.Id);var bw=bW.ToDictionary(x=>x.Id);var ae=aE.ToDictionary(x=>x.Id);var be=bE.ToDictionary(x=>x.Id);var am=aM.ToDictionary(x=>x.Id);var bm=bM.ToDictionary(x=>x.Id);var mismatches=new List<LitigationMismatch>();
        foreach(var id in aw.Keys.Intersect(bw.Keys)){var a=aw[id];var b=bw[id];C("Witness",id,"CaseId",a.CaseId.ToString(),b.CaseId.ToString(),mismatches);C("Witness",id,"Name",a.Name,b.Name,mismatches);C("Witness",id,"Side",a.Side,b.Side,mismatches);C("Witness",id,"Role",a.Role,b.Role,mismatches);C("Witness",id,"ContactInfo",a.ContactInfo,b.ContactInfo,mismatches);C("Witness",id,"SubpoenaStatus",a.SubpoenaStatus,b.SubpoenaStatus,mismatches);C("Witness",id,"OutlineNotes",a.OutlineNotes,b.OutlineNotes,mismatches);C("Witness",id,"Notes",a.Notes,b.Notes,mismatches);}
        foreach(var id in ae.Keys.Intersect(be.Keys)){var a=ae[id];var b=be[id];C("Exhibit",id,"CaseId",a.CaseId.ToString(),b.CaseId.ToString(),mismatches);C("Exhibit",id,"Label",a.Label,b.Label,mismatches);C("Exhibit",id,"Side",a.Side,b.Side,mismatches);C("Exhibit",id,"Description",a.Description,b.Description,mismatches);C("Exhibit",id,"Status",a.Status,b.Status,mismatches);C("Exhibit",id,"Notes",a.Notes,b.Notes,mismatches);}
        foreach(var id in am.Keys.Intersect(bm.Keys)){var a=am[id];var b=bm[id];C("TrialMotion",id,"CaseId",a.CaseId.ToString(),b.CaseId.ToString(),mismatches);C("TrialMotion",id,"Title",a.Title,b.Title,mismatches);C("TrialMotion",id,"FiledBy",a.FiledBy,b.FiledBy,mismatches);C("TrialMotion",id,"FiledDate",a.FiledDate,b.FiledDate,mismatches);C("TrialMotion",id,"Status",a.Status,b.Status,mismatches);C("TrialMotion",id,"Notes",a.Notes,b.Notes,mismatches);}
        var mw=M(aw,bw);var me=M(ae,be);var mm=M(am,bm);return new(mw.Count==0&&me.Count==0&&mm.Count==0&&mismatches.Count==0,aW.Count,bW.Count,aE.Count,bE.Count,aM.Count,bM.Count,mw,me,mm,mismatches.Take(100).ToList());
    }
    private static List<long>M<T>(Dictionary<long,T>a,Dictionary<long,T>b)=>a.Keys.Except(b.Keys).Concat(b.Keys.Except(a.Keys)).Distinct().Order().ToList();
    private static void C(string k,long id,string f,string? a,string? b,List<LitigationMismatch> r){if(!string.Equals(a,b,StringComparison.Ordinal))r.Add(new(k,id,f,a,b));}
}
