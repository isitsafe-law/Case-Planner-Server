using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

public sealed record WorkspaceDashboardMismatch(string Area,string Field,string? SqliteValue,string? SqlServerValue);
public sealed record WorkspaceDashboardReconciliation(bool Matches,long CaseId,List<WorkspaceDashboardMismatch> Mismatches);

public sealed class WorkspaceDashboardReconciliationService(CasePlannerRepository sqlite,SqlServerWorkspaceQuery sql)
{
    public async Task<WorkspaceDashboardReconciliation> CompareAsync(long caseId,CancellationToken token=default)
    {
        var source=await sqlite.GetCaseWorkspaceAsync(caseId);var target=await sql.GetWorkspaceAsync(caseId,null,token);var result=new List<WorkspaceDashboardMismatch>();
        if(source is null||target is null){result.Add(new("Workspace","Exists",(source is not null).ToString(),(target is not null).ToString()));return new(false,caseId,result);}
        C("Case","Id",source.Case.Id,target.Case.Id,result);C("Case","Name",source.Case.CaseName,target.Case.CaseName,result);C("Case","Number",source.Case.CaseNumber,target.Case.CaseNumber,result);
        C("Workspace","Deadlines",source.Deadlines.Count,target.Deadlines.Count,result);C("Workspace","ChecklistItems",source.ChecklistItems.Count,target.ChecklistItems.Count,result);C("Workspace","DiscoveryItems",source.DiscoveryItems.Count,target.DiscoveryItems.Count,result);C("Workspace","PublicationEntries",source.PublicationEntries.Count,target.PublicationEntries.Count,result);C("Workspace","CaseIssueTags",source.CaseIssueTags.Count,target.CaseIssueTags.Count,result);C("Workspace","AvailableIssueTags",source.AvailableIssueTags.Count,target.AvailableIssueTags.Count,result);C("Workspace","CaseNotes",source.CaseNotes.Count,target.CaseNotes.Count,result);C("Workspace","Hearings",source.Hearings.Count,target.Hearings.Count,result);C("Workspace","DocumentExports",source.DocumentExports.Count,target.DocumentExports.Count,result);
        C("Service","WarningLevel",source.ServiceStatus.WarningLevel,target.ServiceStatus.WarningLevel,result);C("Service","Deadline",source.ServiceStatus.ServiceDeadline120,target.ServiceStatus.ServiceDeadline120,result);
        var a=source.OverviewSummary;var b=target.OverviewSummary;C("Dashboard","OverdueDeadlines",a.OverdueDeadlines,b.OverdueDeadlines,result);C("Dashboard","DueIn7Days",a.DueIn7Days,b.DueIn7Days,result);C("Dashboard","DueIn30Days",a.DueIn30Days,b.DueIn30Days,result);C("Dashboard","ActiveCaseCount",a.ActiveCaseCount,b.ActiveCaseCount,result);C("Dashboard","TriageQueue",a.TriageQueue.Count,b.TriageQueue.Count,result);C("Dashboard","ServiceOverdue",a.ServiceOverdue,b.ServiceOverdue,result);C("Dashboard","MissingServiceDeadline",a.MissingServiceDeadline,b.MissingServiceDeadline,result);
        return new(result.Count==0,caseId,result);
    }
    private static void C(string area,string field,object? a,object? b,List<WorkspaceDashboardMismatch> result){var x=Convert.ToString(a);var y=Convert.ToString(b);if(!string.Equals(x,y,StringComparison.Ordinal))result.Add(new(area,field,x,y));}
}
