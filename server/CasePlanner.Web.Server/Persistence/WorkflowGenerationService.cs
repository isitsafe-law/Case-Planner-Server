using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Security;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

public interface IWorkflowGenerationService
{
    Task<(int Added,int Updated)> GenerateDeadlinesAsync(long caseId,CancellationToken token=default);
    Task<int> GenerateChecklistAsync(long caseId,CancellationToken token=default);
    Task<List<WorkTemplateCandidate>> GetCandidatesAsync(long caseId,CancellationToken token=default);
    Task<int> AddSelectionsAsync(long caseId,AddWorkTemplatesRequest request,CancellationToken token=default);
}

public sealed class SqliteWorkflowGenerationService(CasePlannerRepository repository) : IWorkflowGenerationService
{
    public Task<(int Added,int Updated)> GenerateDeadlinesAsync(long caseId,CancellationToken token=default){token.ThrowIfCancellationRequested();return repository.GenerateDeadlinesAsync(caseId);}
    public Task<int> GenerateChecklistAsync(long caseId,CancellationToken token=default){token.ThrowIfCancellationRequested();return repository.GenerateChecklistAsync(caseId);}
    public Task<List<WorkTemplateCandidate>> GetCandidatesAsync(long caseId,CancellationToken token=default){token.ThrowIfCancellationRequested();return repository.GetWorkTemplateCandidatesAsync(caseId);}
    public Task<int> AddSelectionsAsync(long caseId,AddWorkTemplatesRequest request,CancellationToken token=default){token.ThrowIfCancellationRequested();return repository.AddWorkTemplateSelectionsAsync(caseId,request);}
}

public sealed class SqlServerWorkflowGenerationService(
    SqlServerWorkspaceQuery workspaces,
    SqlServerWorkTemplateStore templates,
    SqlServerDeadlineStore deadlines,
    SqlServerChecklistStore checklist,
    SqlServerActivityStore activities,
    IApplicationActorContext actor) : IWorkflowGenerationService
{
    public async Task<(int Added,int Updated)> GenerateDeadlinesAsync(long caseId,CancellationToken token=default)
    {
        var workspace=await RequiredWorkspace(caseId,token);
        if(IsPreWorkflow(workspace.Case))return (0,0);
        var candidates=(await GetCandidatesAsync(caseId,token)).Where(x=>x.Kind=="Deadline"&&x.DueDate is not null).ToList();
        var added=0;var updated=0;
        foreach(var candidate in candidates)
        {
            var existing=workspace.Deadlines.FirstOrDefault(x=>x.SourceTemplateId==candidate.TemplateId);
            if(existing is null)
            {
                if(candidate.IsDuplicate)continue;
                await deadlines.SaveAsync(NewDeadline(caseId,candidate,candidate.DueDate),token);added++;
            }
            else if(existing.History.Count==0&&!string.Equals(existing.DueDate,candidate.DueDate,StringComparison.Ordinal))
            {
                existing.DueDate=candidate.DueDate;existing.Title=candidate.Title;existing.Severity=candidate.Severity??"normal";
                existing.ReasonForChange="Recalculated after the template trigger date changed.";
                await deadlines.SaveAsync(existing,token);updated++;
            }
        }
        if(added+updated>0)await activities.RecordAsync(caseId,"TemplateBatchAdded",$"Refreshed deadline templates: {added} added, {updated} updated",null,token);
        return (added,updated);
    }

    public async Task<int> GenerateChecklistAsync(long caseId,CancellationToken token=default)
    {
        var workspace=await RequiredWorkspace(caseId,token);
        if(IsPreWorkflow(workspace.Case))return 0;
        var candidates=(await GetCandidatesAsync(caseId,token)).Where(x=>x.Kind=="Task"&&!x.IsDuplicate).ToList();
        foreach(var candidate in candidates)await checklist.SaveAsync(NewTask(caseId,candidate,candidate.DueDate),token);
        if(candidates.Count>0)await activities.RecordAsync(caseId,"TemplateBatchAdded",$"Generated {candidates.Count} checklist template item(s)",null,token);
        return candidates.Count;
    }

    public async Task<List<WorkTemplateCandidate>> GetCandidatesAsync(long caseId,CancellationToken token=default)
    {
        var ws=await RequiredWorkspace(caseId,token);var result=new List<WorkTemplateCandidate>();var today=DateOnly.FromDateTime(DateTime.Today);
        var workflow=string.IsNullOrWhiteSpace(ws.Case.CaseStatus)?ws.Case.Status:ws.Case.CaseStatus;
        foreach(var template in await templates.GetChecklistAsync(token))
        {
            if(!template.Active)continue;
            if(template.TriggerType=="Stage"&&!string.Equals(template.Stage,workflow,StringComparison.OrdinalIgnoreCase))continue;
            if(template.TriggerType=="IssueTag"&&!ws.CaseIssueTags.Any(x=>string.Equals(x.TagName,template.IssueTagName,StringComparison.OrdinalIgnoreCase)))continue;
            foreach(var item in template.Items)
            {
                var id=$"{template.Name}:{item.SortOrder}";var stage=item.Phase??workflow;
                var duplicate=ws.ChecklistItems.FirstOrDefault(x=>x.SourceTemplateId==id||(string.Equals(x.Phase,stage,StringComparison.OrdinalIgnoreCase)&&string.Equals(x.Task,item.Task,StringComparison.OrdinalIgnoreCase)));
                result.Add(new(){Kind="Task",TemplateId=id,TemplateVersion=1,Title=item.Task,Stage=stage,DueDate=item.DueOffsetDays is { } offset?today.AddDays(offset).ToString("yyyy-MM-dd"):null,IsDuplicate=duplicate is not null,DuplicateReason=duplicate is null?null:$"Matches {duplicate.Status.ToLowerInvariant()} task: {duplicate.Task}"});
            }
        }
        foreach(var template in await templates.GetDeadlinesAsync(token))
        {
            if(!template.Active)continue;
            var anchor=template.TriggerField switch{"filing_date"=>Date(ws.Case.FilingDate),"trial_date"=>Date(ws.Case.TrialDate),"service_perfected_date"=>Date(ws.Case.ServicePerfectedDate),_=>null};
            var duplicate=ws.Deadlines.FirstOrDefault(x=>x.SourceTemplateId==template.Id.ToString()||string.Equals(x.Title,template.Title,StringComparison.OrdinalIgnoreCase));
            result.Add(new(){Kind="Deadline",TemplateId=template.Id.ToString(),TemplateVersion=3,Title=template.Title,Stage=workflow,Severity=template.Severity,DueDate=anchor?.AddDays(template.OffsetDays).ToString("yyyy-MM-dd"),IsDuplicate=duplicate is not null,DuplicateReason=duplicate is null?null:$"Matches {duplicate.Status.ToLowerInvariant()} deadline: {duplicate.Title}"});
        }
        return result;
    }

    public async Task<int> AddSelectionsAsync(long caseId,AddWorkTemplatesRequest request,CancellationToken token=default)
    {
        var candidates=(await GetCandidatesAsync(caseId,token)).ToDictionary(x=>$"{x.Kind}:{x.TemplateId}",StringComparer.OrdinalIgnoreCase);var added=0;
        foreach(var selection in request.Items)
        {
            if(!candidates.TryGetValue($"{selection.Kind}:{selection.TemplateId}",out var c)||(c.IsDuplicate&&!selection.AllowDuplicate))continue;
            if(c.Kind=="Task")await checklist.SaveAsync(NewTask(caseId,c,selection.DueDate),token);
            else await deadlines.SaveAsync(NewDeadline(caseId,c,selection.DueDate),token);
            added++;
        }
        if(added>0)await activities.RecordAsync(caseId,"TemplateBatchAdded",$"Added {added} task/deadline template item(s) after review",null,token);
        return added;
    }

    private async Task<CaseWorkspaceResponse> RequiredWorkspace(long caseId,CancellationToken token)=>
        await workspaces.GetWorkspaceAsync(caseId,null,token)??throw new InvalidOperationException("Case not found.");
    private static bool IsPreWorkflow(CaseRecord c)=>c.Status is "Triage" or "Pipeline"||c.CaseStatus is "Triage" or "Pipeline";
    private static DateOnly? Date(string? value)=>DateOnly.TryParse(value,out var date)?date:null;
    private string Now()=>DateTime.UtcNow.ToString("O");
    private ChecklistItemRecord NewTask(long caseId,WorkTemplateCandidate c,string? due)=>new(){CaseId=caseId,Phase=c.Stage,Task=c.Title,DueDate=due,Status="Not Started",SourceType=$"Template:{c.TemplateId}",SourceKind="StageTemplate",SourceTemplateId=c.TemplateId,SourceTemplateVersion=c.TemplateVersion,SourceStage=c.Stage,GeneratedAt=Now(),GeneratedBy=actor.AuditLabel,IsManual=false};
    private DeadlineItem NewDeadline(long caseId,WorkTemplateCandidate c,string? due)=>new(){CaseId=caseId,Title=c.Title,DueDate=due,Status="Open",Severity=c.Severity??"normal",SourceType=$"Computed:{c.TemplateId}",SourceKind="DeadlineTemplate",SourceTemplateId=c.TemplateId,SourceTemplateVersion=c.TemplateVersion,SourceStage=c.Stage,GeneratedAt=Now(),GeneratedBy=actor.AuditLabel,IsManual=false};
}
