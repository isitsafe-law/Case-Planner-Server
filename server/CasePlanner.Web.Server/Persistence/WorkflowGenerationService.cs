using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Security;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

public interface IWorkflowGenerationService
{
    Task<(int Added,int Updated)> GenerateDeadlinesAsync(long caseId,CancellationToken token=default);
    Task<int> GenerateChecklistAsync(long caseId,CancellationToken token=default);
    Task<List<WorkTemplateCandidate>> GetCandidatesAsync(long caseId,CancellationToken token=default);
    Task<List<WorkTemplateCandidate>> GetEventPreparationCandidatesAsync(long caseId,long eventId,CancellationToken token=default);
    Task<EventPreparationDateRecalculationPreview> PreviewEventPreparationDateRecalculationAsync(long caseId,long eventId,string proposedStartDate,CancellationToken token=default);
    Task<EventPreparationDateRecalculationPreview> ApplyEventPreparationDateRecalculationAsync(long caseId,long eventId,string proposedStartDate,CancellationToken token=default);
    Task<int> AddSelectionsAsync(long caseId,AddWorkTemplatesRequest request,CancellationToken token=default);
    Task<int> AddEventPreparationSelectionsAsync(long caseId,long eventId,AddWorkTemplatesRequest request,CancellationToken token=default);
}

public sealed class SqliteWorkflowGenerationService(CasePlannerRepository repository) : IWorkflowGenerationService
{
    public Task<(int Added,int Updated)> GenerateDeadlinesAsync(long caseId,CancellationToken token=default){token.ThrowIfCancellationRequested();return repository.GenerateDeadlinesAsync(caseId);}
    public Task<int> GenerateChecklistAsync(long caseId,CancellationToken token=default){token.ThrowIfCancellationRequested();return repository.GenerateChecklistAsync(caseId);}
    public Task<List<WorkTemplateCandidate>> GetCandidatesAsync(long caseId,CancellationToken token=default){token.ThrowIfCancellationRequested();return repository.GetWorkTemplateCandidatesAsync(caseId);}
    public Task<List<WorkTemplateCandidate>> GetEventPreparationCandidatesAsync(long caseId,long eventId,CancellationToken token=default){token.ThrowIfCancellationRequested();return repository.GetEventPreparationCandidatesAsync(caseId,eventId);}
    public Task<EventPreparationDateRecalculationPreview> PreviewEventPreparationDateRecalculationAsync(long caseId,long eventId,string proposedStartDate,CancellationToken token=default){token.ThrowIfCancellationRequested();return repository.PreviewEventPreparationDateRecalculationAsync(caseId,eventId,proposedStartDate);}
    public Task<EventPreparationDateRecalculationPreview> ApplyEventPreparationDateRecalculationAsync(long caseId,long eventId,string proposedStartDate,CancellationToken token=default){token.ThrowIfCancellationRequested();return repository.ApplyEventPreparationDateRecalculationAsync(caseId,eventId,proposedStartDate);}
    public Task<int> AddSelectionsAsync(long caseId,AddWorkTemplatesRequest request,CancellationToken token=default){token.ThrowIfCancellationRequested();return repository.AddWorkTemplateSelectionsAsync(caseId,request);}
    public Task<int> AddEventPreparationSelectionsAsync(long caseId,long eventId,AddWorkTemplatesRequest request,CancellationToken token=default){token.ThrowIfCancellationRequested();return repository.AddEventPreparationSelectionsAsync(caseId,eventId,request);}
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
                result.Add(new(){Kind="Task",TemplateId=id,TemplateVersion=1,Title=item.Task,Stage=stage,RelativeOffsetDays=item.DueOffsetDays,DueDate=item.DueOffsetDays is { } offset?today.AddDays(offset).ToString("yyyy-MM-dd"):null,IsDuplicate=duplicate is not null,DuplicateReason=duplicate is null?null:$"Matches {duplicate.Status.ToLowerInvariant()} task: {duplicate.Task}"});
            }
        }
        foreach(var template in await templates.GetDeadlinesAsync(token))
        {
            if(!template.Active)continue;
            var anchor=template.TriggerField switch{"filing_date"=>Date(ws.Case.FilingDate),"trial_date"=>Date(ws.Case.TrialDate),"service_perfected_date"=>Date(ws.Case.ServicePerfectedDate),_=>null};
            var duplicate=ws.Deadlines.FirstOrDefault(x=>x.SourceTemplateId==template.Id.ToString()||string.Equals(x.Title,template.Title,StringComparison.OrdinalIgnoreCase));
            result.Add(new(){Kind="Deadline",TemplateId=template.Id.ToString(),TemplateVersion=3,Title=template.Title,Stage=workflow,Severity=template.Severity,RelativeOffsetDays=template.OffsetDays,DueDate=anchor?.AddDays(template.OffsetDays).ToString("yyyy-MM-dd"),IsDuplicate=duplicate is not null,DuplicateReason=duplicate is null?null:$"Matches {duplicate.Status.ToLowerInvariant()} deadline: {duplicate.Title}"});
        }
        return result;
    }

    public async Task<List<WorkTemplateCandidate>> GetEventPreparationCandidatesAsync(long caseId,long eventId,CancellationToken token=default)
    {
        var ws=await RequiredWorkspace(caseId,token);var ev=ws.Hearings.FirstOrDefault(x=>x.Id==eventId);
        if(ev is null)throw new ArgumentException("The proceeding does not belong to this case.");
        if(!DateOnly.TryParse(ev.HearingDate,out var eventDate))return [];
        var candidates=await GetCandidatesAsync(caseId,token);var linkedTasks=ws.ChecklistItems.Where(x=>x.RelatedEventId==eventId).ToList();var linkedDeadlines=ws.Deadlines.Where(x=>x.RelatedEventId==eventId).ToList();
        foreach(var candidate in candidates)
        {
            if(candidate.RelativeOffsetDays is { } offset)candidate.DueDate=eventDate.AddDays(offset).ToString("yyyy-MM-dd");
            var linkedStatus = candidate.Kind=="Task"
                ? linkedTasks.FirstOrDefault(x=>x.SourceTemplateId==candidate.TemplateId)?.Status
                : linkedDeadlines.FirstOrDefault(x=>x.SourceTemplateId==candidate.TemplateId)?.Status;
            candidate.IsDuplicate=linkedStatus is not null;
            candidate.DuplicateReason=linkedStatus is null?null:$"Matches {linkedStatus.ToLowerInvariant()} {candidate.Kind.ToLowerInvariant()} for this event";
        }
        return candidates;
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

    public async Task<int> AddEventPreparationSelectionsAsync(long caseId,long eventId,AddWorkTemplatesRequest request,CancellationToken token=default)
    {
        var ws=await RequiredWorkspace(caseId,token);
        var ev=ws.Hearings.FirstOrDefault(x=>x.Id==eventId);
        if(ev is null) throw new ArgumentException("The proceeding does not belong to this case.");
        var candidates=(await GetEventPreparationCandidatesAsync(caseId,eventId,token)).ToDictionary(x=>$"{x.Kind}:{x.TemplateId}",StringComparer.OrdinalIgnoreCase);var added=0;
        foreach(var selection in request.Items)
        {
            if(!candidates.TryGetValue($"{selection.Kind}:{selection.TemplateId}",out var c)||(c.IsDuplicate&&!selection.AllowDuplicate))continue;
            if(c.Kind=="Task") { var item=NewTask(caseId,c,selection.DueDate);item.RelatedEventId=eventId;await checklist.SaveAsync(item,token); }
            else { var item=NewDeadline(caseId,c,selection.DueDate);item.RelatedEventId=eventId;await deadlines.SaveAsync(item,token); }
            added++;
        }
        if(added>0)await activities.RecordAsync(caseId,"EventPreparationTemplateApplied",$"Added {added} preparation item(s) for event {eventId}",null,token);
        return added;
    }

    public async Task<EventPreparationDateRecalculationPreview> PreviewEventPreparationDateRecalculationAsync(long caseId,long eventId,string proposedStartDate,CancellationToken token=default)
    {
        var ws=await RequiredWorkspace(caseId,token);var ev=ws.Hearings.FirstOrDefault(x=>x.Id==eventId)??throw new ArgumentException("The proceeding does not belong to this case.");
        return EventPreparationDateRecalculator.Build(caseId,eventId,ev.HearingDate,proposedStartDate,ws.ChecklistItems.Where(x=>x.RelatedEventId==eventId),ws.Deadlines.Where(x=>x.RelatedEventId==eventId));
    }

    public async Task<EventPreparationDateRecalculationPreview> ApplyEventPreparationDateRecalculationAsync(long caseId,long eventId,string proposedStartDate,CancellationToken token=default)
    {
        var ws=await RequiredWorkspace(caseId,token);var ev=ws.Hearings.FirstOrDefault(x=>x.Id==eventId)??throw new ArgumentException("The proceeding does not belong to this case.");
        var preview=EventPreparationDateRecalculator.Build(caseId,eventId,ev.HearingDate,proposedStartDate,ws.ChecklistItems.Where(x=>x.RelatedEventId==eventId),ws.Deadlines.Where(x=>x.RelatedEventId==eventId));
        foreach(var change in preview.Changes.Where(x=>x.WillMove))
        {
            if(change.Kind=="Task"){var item=ws.ChecklistItems.First(x=>x.Id==change.WorkItemId);item.DueDate=change.ProposedDueDate;item.IsDateRecalculation=true;await checklist.SaveAsync(item,token);}
            else{var item=ws.Deadlines.First(x=>x.Id==change.WorkItemId);item.DueDate=change.ProposedDueDate;item.ReasonForChange=$"Recalculated after proceeding date changed from {ev.HearingDate} to {proposedStartDate}.";await deadlines.SaveAsync(item,token);}
        }
        if(preview.Changes.Any(x=>x.WillMove))await activities.RecordAsync(caseId,"EventPreparationDatesRecalculated",$"Recalculated preparation dates for event {eventId} from {ev.HearingDate} to {proposedStartDate}",null,token);
        return preview;
    }

    private async Task<CaseWorkspaceResponse> RequiredWorkspace(long caseId,CancellationToken token)=>
        await workspaces.GetWorkspaceAsync(caseId,null,token)??throw new InvalidOperationException("Case not found.");
    private static bool IsPreWorkflow(CaseRecord c)=>WorkflowStatusRules.IsPreFiling(c.Status,c.CaseStatus);
    private static DateOnly? Date(string? value)=>DateOnly.TryParse(value,out var date)?date:null;
    private string Now()=>DateTime.UtcNow.ToString("O");
    private ChecklistItemRecord NewTask(long caseId,WorkTemplateCandidate c,string? due)=>new(){CaseId=caseId,Phase=c.Stage,Task=c.Title,DueDate=due,Status="Not Started",SourceType=$"Template:{c.TemplateId}",SourceKind="StageTemplate",SourceTemplateId=c.TemplateId,SourceTemplateVersion=c.TemplateVersion,SourceStage=c.Stage,GeneratedAt=Now(),GeneratedBy=actor.AuditLabel,IsManual=false};
    private DeadlineItem NewDeadline(long caseId,WorkTemplateCandidate c,string? due)=>new(){CaseId=caseId,Title=c.Title,DueDate=due,Status="Open",Severity=c.Severity??"normal",SourceType=$"Computed:{c.TemplateId}",SourceKind="DeadlineTemplate",SourceTemplateId=c.TemplateId,SourceTemplateVersion=c.TemplateVersion,SourceStage=c.Stage,GeneratedAt=Now(),GeneratedBy=actor.AuditLabel,IsManual=false};
}

internal static class EventPreparationDateRecalculator
{
    public static EventPreparationDateRecalculationPreview Build(long caseId,long eventId,string? currentStart,string proposedStart,IEnumerable<ChecklistItemRecord> tasks,IEnumerable<DeadlineItem> deadlines)
    {
        if(!DateOnly.TryParse(proposedStart,out var proposed))throw new ArgumentException("A valid proposed event date is required.");
        if(!DateOnly.TryParse(currentStart,out var current))throw new ArgumentException("The proceeding must have a valid current start date.");
        var preview=new EventPreparationDateRecalculationPreview{CaseId=caseId,EventId=eventId,CurrentStartDate=currentStart,ProposedStartDate=proposedStart};
        foreach(var item in tasks)
        {
            var completed=item.Status is "Done" or "Complete"||item.CompletedAt is not null;var manual=item.IsManual;var next=item.DueDate;
            if(!completed&&!manual&&DateOnly.TryParse(item.DueDate,out var due))next=proposed.AddDays(due.DayNumber-current.DayNumber).ToString("yyyy-MM-dd");
            preview.Changes.Add(new(){Kind="Task",WorkItemId=item.Id,Title=item.Task,CurrentDueDate=item.DueDate,ProposedDueDate=next,IsManualOverride=manual,IsCompleted=completed,WillMove=!completed&&!manual&&next!=item.DueDate});
        }
        foreach(var item in deadlines)
        {
            var completed=item.Status is "Done" or "Complete"||item.CompletedAt is not null;var manual=item.IsManual||item.History.Count>0;var next=item.DueDate;
            if(!completed&&!manual&&DateOnly.TryParse(item.DueDate,out var due))next=proposed.AddDays(due.DayNumber-current.DayNumber).ToString("yyyy-MM-dd");
            preview.Changes.Add(new(){Kind="Deadline",WorkItemId=item.Id,Title=item.Title,CurrentDueDate=item.DueDate,ProposedDueDate=next,IsManualOverride=manual,IsCompleted=completed,WillMove=!completed&&!manual&&next!=item.DueDate});
        }
        return preview;
    }
}
