using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

public sealed record WorkflowGenerationMismatch(long CaseId,string Field,string SqliteValue,string SqlServerValue);
public sealed record WorkflowGenerationReconciliation(bool Matches,int CasesCompared,List<WorkflowGenerationMismatch> Mismatches);

public sealed class WorkflowGenerationReconciliationService(
    CasePlannerRepository sqlite,
    SqlServerWorkflowGenerationService sql,
    SqliteCaseCatalogReader cases)
{
    public async Task<WorkflowGenerationReconciliation> CompareAsync(CancellationToken token=default)
    {
        var mismatches=new List<WorkflowGenerationMismatch>();var rows=await cases.GetCasesAsync(new(IncludeClosed:true),token);
        foreach(var c in rows)
        {
            var left=await sqlite.GetWorkTemplateCandidatesAsync(c.Id);var right=await sql.GetCandidatesAsync(c.Id,token);
            Compare(c.Id,"CandidateKeys",Key(left),Key(right));
            Compare(c.Id,"DuplicateKeys",Key(left.Where(x=>x.IsDuplicate)),Key(right.Where(x=>x.IsDuplicate)));
            Compare(c.Id,"DueDates",Due(left),Due(right));
        }
        return new(mismatches.Count==0,rows.Count,mismatches.Take(100).ToList());
        void Compare(long caseId,string field,string a,string b){if(!string.Equals(a,b,StringComparison.Ordinal))mismatches.Add(new(caseId,field,a,b));}
    }
    private static string Key(IEnumerable<CasePlanner.Web.Server.Models.WorkTemplateCandidate> rows)=>string.Join("|",rows.Select(x=>$"{x.Kind}:{x.TemplateId}:{x.Title}").Order(StringComparer.Ordinal));
    private static string Due(IEnumerable<CasePlanner.Web.Server.Models.WorkTemplateCandidate> rows)=>string.Join("|",rows.Select(x=>$"{x.Kind}:{x.TemplateId}:{x.DueDate}").Order(StringComparer.Ordinal));
}
