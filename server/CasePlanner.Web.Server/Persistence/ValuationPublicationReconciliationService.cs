using System.Globalization;

namespace CasePlanner.Web.Server.Persistence;

public sealed record ValuationPublicationMismatch(string Kind,long Id,string Field,string? SqliteValue,string? SqlServerValue);
public sealed record ValuationPublicationReconciliation(bool Matches,int SqlitePositions,int SqlServerPositions,int SqliteSales,int SqlServerSales,int SqlitePublications,int SqlServerPublications,List<long> MissingPositionIds,List<long> MissingSaleIds,List<long> MissingPublicationIds,List<ValuationPublicationMismatch> Mismatches);

public sealed class ValuationPublicationReconciliationService(SqliteValuationPositionStore sp,SqlServerValuationPositionStore tp,SqliteComparableSaleStore ss,SqlServerComparableSaleStore ts,SqlitePublicationEntryStore se,SqlServerPublicationEntryStore te)
{
    public async Task<ValuationPublicationReconciliation> CompareAsync(CancellationToken token=default)
    {
        var ap=await sp.GetAsync(null,token);var bp=await tp.GetAsync(null,token);var ass=await ss.GetAsync(null,token);var bs=await ts.GetAsync(null,token);var ae=await se.GetAsync(null,token);var be=await te.GetAsync(null,token);var p=ap.ToDictionary(x=>x.Id);var q=bp.ToDictionary(x=>x.Id);var s=ass.ToDictionary(x=>x.Id);var t=bs.ToDictionary(x=>x.Id);var e=ae.ToDictionary(x=>x.Id);var f=be.ToDictionary(x=>x.Id);var mismatches=new List<ValuationPublicationMismatch>();
        foreach(var id in p.Keys.Intersect(q.Keys)){var a=p[id];var b=q[id];C("ValuationPosition",id,"CaseId",V(a.CaseId),V(b.CaseId),mismatches);C("ValuationPosition",id,"Side",a.Side,b.Side,mismatches);C("ValuationPosition",id,"AppraiserName",a.AppraiserName,b.AppraiserName,mismatches);C("ValuationPosition",id,"AppraisedValue",V(a.AppraisedValue),V(b.AppraisedValue),mismatches);C("ValuationPosition",id,"ValueDate",a.ValueDate,b.ValueDate,mismatches);C("ValuationPosition",id,"Methodology",a.Methodology,b.Methodology,mismatches);C("ValuationPosition",id,"Notes",a.Notes,b.Notes,mismatches);C("ValuationPosition",id,"UpdatedAt",a.UpdatedAt,b.UpdatedAt,mismatches);}
        foreach(var id in s.Keys.Intersect(t.Keys)){var a=s[id];var b=t[id];C("ComparableSale",id,"CaseId",V(a.CaseId),V(b.CaseId),mismatches);C("ComparableSale",id,"Side",a.Side,b.Side,mismatches);C("ComparableSale",id,"SaleDescription",a.SaleDescription,b.SaleDescription,mismatches);C("ComparableSale",id,"SalePrice",V(a.SalePrice),V(b.SalePrice),mismatches);C("ComparableSale",id,"SaleDate",a.SaleDate,b.SaleDate,mismatches);C("ComparableSale",id,"SizeAcres",V(a.SizeAcres),V(b.SizeAcres),mismatches);C("ComparableSale",id,"AdjustmentNotes",a.AdjustmentNotes,b.AdjustmentNotes,mismatches);C("ComparableSale",id,"Notes",a.Notes,b.Notes,mismatches);}
        foreach(var id in e.Keys.Intersect(f.Keys)){var a=e[id];var b=f[id];C("PublicationEntry",id,"CaseId",V(a.CaseId),V(b.CaseId),mismatches);C("PublicationEntry",id,"PublicationNumber",a.PublicationNumber,b.PublicationNumber,mismatches);C("PublicationEntry",id,"PublicationDate",a.PublicationDate,b.PublicationDate,mismatches);C("PublicationEntry",id,"Newspaper",a.Newspaper,b.Newspaper,mismatches);C("PublicationEntry",id,"ProofFiled",V(a.ProofFiled),V(b.ProofFiled),mismatches);C("PublicationEntry",id,"ProofFiledDate",a.ProofFiledDate,b.ProofFiledDate,mismatches);C("PublicationEntry",id,"ServiceResolved",V(a.ServiceResolved),V(b.ServiceResolved),mismatches);C("PublicationEntry",id,"Notes",a.Notes,b.Notes,mismatches);}
        var mp=M(p,q);var ms=M(s,t);var me=M(e,f);return new(mp.Count==0&&ms.Count==0&&me.Count==0&&mismatches.Count==0,ap.Count,bp.Count,ass.Count,bs.Count,ae.Count,be.Count,mp,ms,me,mismatches.Take(100).ToList());
    }
    private static string? V(object? value)=>value is null?null:Convert.ToString(value,CultureInfo.InvariantCulture);
    private static List<long>M<T>(Dictionary<long,T>a,Dictionary<long,T>b)=>a.Keys.Except(b.Keys).Concat(b.Keys.Except(a.Keys)).Distinct().Order().ToList();
    private static void C(string k,long id,string field,string? a,string? b,List<ValuationPublicationMismatch> r){if(!string.Equals(a,b,StringComparison.Ordinal))r.Add(new(k,id,field,a,b));}
}
