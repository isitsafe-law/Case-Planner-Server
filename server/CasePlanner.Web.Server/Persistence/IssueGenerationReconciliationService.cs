using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

public sealed record IssueGenerationMismatch(string Kind,long Id,string Field,string? SqliteValue,string? SqlServerValue);
public sealed record IssueGenerationReconciliation(
    bool Matches,int SqliteAssignments,int SqlServerAssignments,
    List<IssueGenerationMismatch> Mismatches);

// Compares case<->issue-tag assignments between SQLite and SQL Server ahead of cutover. Used to
// compare rendered discovery-generation snapshots too, but that table's C# consumers were retired
// in build-plan step 7 (the unified document platform's document_generations table is the audit
// trail now), and the table itself was later dropped entirely once confirmed empty everywhere -
// see 026_retire_discovery_generations.sql.
public sealed class IssueGenerationReconciliationService(
    SqliteCaseCatalogReader sqliteCases,
    SqliteIssueTagStore sqliteTags,
    SqlServerIssueTagStore sqlTags)
{
    public async Task<IssueGenerationReconciliation> CompareAsync(CancellationToken token=default)
    {
        var mismatches=new List<IssueGenerationMismatch>();var leftTags=new Dictionary<long,CasePlanner.Web.Server.Models.CaseIssueTagRecord>();var rightTags=new Dictionary<long,CasePlanner.Web.Server.Models.CaseIssueTagRecord>();
        foreach(var c in await sqliteCases.GetCasesAsync(new(IncludeClosed:true),token))
        {
            foreach(var x in await sqliteTags.GetCaseTagsAsync(c.Id,token))leftTags[x.Id]=x;
            foreach(var x in await sqlTags.GetCaseTagsAsync(c.Id,token))rightTags[x.Id]=x;
        }
        foreach(var id in leftTags.Keys.Except(rightTags.Keys))mismatches.Add(new("IssueTag",id,"Record","Present","Missing"));
        foreach(var id in rightTags.Keys.Except(leftTags.Keys))mismatches.Add(new("IssueTag",id,"Record","Missing","Present"));
        foreach(var id in leftTags.Keys.Intersect(rightTags.Keys))
        {
            var a=leftTags[id];var b=rightTags[id];C("IssueTag",id,"CaseId",a.CaseId.ToString(),b.CaseId.ToString());C("IssueTag",id,"IssueTagId",a.IssueTagId.ToString(),b.IssueTagId.ToString());C("IssueTag",id,"TagName",a.TagName,b.TagName);C("IssueTag",id,"Notes",a.Notes,b.Notes);
        }
        return new(mismatches.Count==0,leftTags.Count,rightTags.Count,mismatches.Take(100).ToList());
        void C(string kind,long id,string field,string? a,string? b){if(!string.Equals(a,b,StringComparison.Ordinal))mismatches.Add(new(kind,id,field,a,b));}
    }
}
