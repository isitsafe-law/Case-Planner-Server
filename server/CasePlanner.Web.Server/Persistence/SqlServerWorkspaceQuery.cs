using System.Data.Common;
using System.Globalization;
using CasePlanner.Data;
using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;
using Microsoft.Data.SqlClient;

namespace CasePlanner.Web.Server.Persistence;

public sealed class SqlServerWorkspaceQuery(
    IDatabaseConnectionFactory connections,SqlServerCaseCatalogReader cases,SqlServerDeadlineStore deadlines,
    SqlServerChecklistStore checklist,SqlServerDiscoveryTrackingStore discovery,SqlServerCaseNoteStore notes,
    SqlServerHearingStore hearings,SqlServerPublicationEntryStore publicationEntries,
    SqlServerActivityStore activities,SqlServerDocumentExportStore documents,SqlServerIssueTagStore issueTags) : IOperationalWorkspaceQuery
{
    public async Task<CaseWorkspaceResponse?> GetWorkspaceAsync(long caseId,IReadOnlySet<long>? visibleCaseIds=null,CancellationToken token=default)
    {
        if(visibleCaseIds is not null&&!visibleCaseIds.Contains(caseId))return null;
        var record=(await cases.GetCasesAsync(new(IncludeClosed:true),token)).FirstOrDefault(x=>x.Id==caseId);
        if(record is null)return null;
        var publication=await GetPublicationAsync(caseId,token)??new PublicationRecord{CaseId=caseId};
        var dashboard=await GetDashboardAsync(visibleCaseIds,token);
        return new()
        {
            Case=record,Deadlines=await deadlines.GetAsync(caseId,token),ChecklistItems=await checklist.GetAsync(caseId,token),
            DiscoveryItems=await discovery.GetAsync(caseId,token),PublicationEntries=await publicationEntries.GetAsync(caseId,token),
            Publication=publication,AvailableIssueTags=await issueTags.GetCatalogAsync(token),CaseIssueTags=await issueTags.GetCaseTagsAsync(caseId,token),
            CaseNotes=await notes.GetAsync(caseId,token),Hearings=await hearings.GetAsync(caseId,token),
            DocumentExports=await documents.GetAsync(caseId,token),ServiceStatus=ServiceStatusEngine.Build(record,publication),
            OverviewSummary=dashboard
        };
    }

    public async Task<DashboardData> GetDashboardAsync(IReadOnlySet<long>? visibleCaseIds=null,CancellationToken token=default)
    {
        bool Visible(long id)=>visibleCaseIds is null||visibleCaseIds.Contains(id);
        var allCases=(await cases.GetCasesAsync(new(IncludeClosed:true),token)).Where(x=>Visible(x.Id)).ToList();
        var allDeadlines=(await deadlines.GetAsync(null,token)).Where(x=>Visible(x.CaseId)).ToList();
        var allChecklist=(await checklist.GetAsync(null,token)).Where(x=>Visible(x.CaseId)).ToList();
        var allDiscovery=(await discovery.GetAsync(null,token)).Where(x=>Visible(x.CaseId)).ToList();
        var allHearings=(await hearings.GetAsync(null,token)).Where(x=>Visible(x.CaseId)).ToList();
        var publications=(await GetPublicationsAsync(null,token)).Where(x=>Visible(x.CaseId)).ToList();
        var postures=(await GetPosturesAsync(null,token)).Where(x=>Visible(x.CaseId)).ToDictionary(x=>x.CaseId);
        var activity=(await activities.GetAsync(null,token)).Where(x=>Visible(x.CaseId)).GroupBy(x=>x.CaseId)
            .ToDictionary(x=>x.Key,x=>x.Max(y=>y.OccurredAt));
        var deadlineGroups=allDeadlines.GroupBy(x=>x.CaseId).ToDictionary(x=>x.Key,x=>(IReadOnlyList<DeadlineItem>)x.ToList());
        foreach(var c in allCases)
        {
            var last=activity.GetValueOrDefault(c.Id)??c.UpdatedAt;
            var attention=CaseAttentionEngine.Compute(deadlineGroups.GetValueOrDefault(c.Id,[]),last,c.Status);
            c.AttentionStatus=attention.Status;c.NextDeadlineDate=attention.NextDeadlineDate;c.NextDeadlineTitle=attention.NextDeadlineTitle;c.LastActivityAt=last;
        }
        return DashboardComposer.Compose(allCases,allDeadlines,allChecklist,allDiscovery,allHearings,publications,postures);
    }

    public async Task<List<ServiceQueueItem>> GetServiceQueueAsync(IReadOnlySet<long>? visibleCaseIds=null,CancellationToken token=default)
    {
        bool Visible(long id)=>visibleCaseIds is null||visibleCaseIds.Contains(id);
        var allCases=(await cases.GetCasesAsync(new(IncludeClosed:true),token)).Where(x=>Visible(x.Id)).ToList();
        var publications=(await GetPublicationsAsync(null,token)).Where(x=>Visible(x.CaseId)).ToList();
        return ServiceStatusEngine.BuildQueue(allCases,publications).OrderBy(x=>Date(x.ServiceDeadline120)??DateOnly.MaxValue).ThenBy(x=>x.CaseName).ToList();
    }

    public async Task<AttorneyDashboardResponse> GetAttorneyDashboardAsync(AttorneyDashboardFilters filters,IReadOnlySet<long>? visibleCaseIds=null,CancellationToken token=default)
    {
        bool Visible(long id)=>visibleCaseIds is null||visibleCaseIds.Contains(id);
        var allCases=(await cases.GetCasesAsync(new(IncludeClosed:true),token)).Where(x=>Visible(x.Id)).ToList();
        var allDeadlines=(await deadlines.GetAsync(null,token)).Where(x=>Visible(x.CaseId)).ToList();
        var allHearings=(await hearings.GetAsync(null,token)).Where(x=>Visible(x.CaseId)).ToList();
        var postures=(await GetPosturesAsync(null,token)).Where(x=>Visible(x.CaseId)).ToList();
        return AttorneyDashboardComposer.Compose(allCases,allDeadlines,allHearings,postures,filters);
    }

    public async Task<List<UpcomingWorkItemRecord>> GetUpcomingWorkAsync(string? type,string? urgency,int limit,IReadOnlySet<long>? visibleCaseIds=null,CancellationToken token=default)
    {
        var today=DateOnly.FromDateTime(DateTime.Today);bool Visible(long id)=>visibleCaseIds is null||visibleCaseIds.Contains(id);
        var caseMap=(await cases.GetCasesAsync(new(IncludeClosed:true),token)).Where(x=>Visible(x.Id)&&x.Status is not ("Closed" or "Complete")).ToDictionary(x=>x.Id);
        var rows=new List<UpcomingWorkItemRecord>();
        void Add(string key,long caseId,string title,string itemType,string? dueDate,string tab)
        {
            if(!caseMap.TryGetValue(caseId,out var c)||(type is not null&&type!="all"&&type!=itemType))return;
            if(Date(c.DeferredUntil) is { } deferred&&deferred>today)return;if(c.CaseStatus=="Pipeline"&&itemType!="service")return;
            var due=Date(dueDate);var days=due?.DayNumber-today.DayNumber;var level=days is null?"No Due Date":days<0?"Overdue":days==0?"Due Today":days<=7?"Next 7 Days":days<=14?"Next 14 Days":days<=30?"Next 30 Days":"Later";
            var requested=urgency??"All Open";if(requested!="All Open"&&requested!=level&&!(requested=="Next 7 Days"&&days is >=0 and <=7)&&!(requested=="Next 14 Days"&&days is >=0 and <=14)&&!(requested=="Next 30 Days"&&days is >=0 and <=30))return;
            rows.Add(new(){Key=key,CaseId=caseId,CaseName=c.CaseName,Title=title,Type=itemType,DueDate=dueDate,Urgency=level,IsOverdue=days<0,Tab=tab});
        }
        foreach(var x in (await checklist.GetAsync(null,token)).Where(x=>x.Status is not("Done" or "Complete" or "N/A")))Add($"task-{x.Id}",x.CaseId,x.Task,"task",x.DueDate,"checklist");
        foreach(var x in (await deadlines.GetAsync(null,token)).Where(x=>x.Status is not("Done" or "Complete")))Add($"deadline-{x.Id}",x.CaseId,x.Title,"deadline",x.DueDate,"deadlines");
        foreach(var x in (await discovery.GetAsync(null,token)).Where(x=>!x.Status.Contains("complete",StringComparison.OrdinalIgnoreCase)&&!x.Status.Contains("cancel",StringComparison.OrdinalIgnoreCase)))Add($"discovery-{x.Id}",x.CaseId,x.RequestTitle??$"{x.Direction} {x.DiscoveryType}","discovery",x.FollowUpDate??x.DueDate,"discovery");
        foreach(var x in await GetServiceQueueAsync(visibleCaseIds,token))if(!x.ServicePerfected)Add($"service-{x.CaseId}",x.CaseId,x.ServiceDeadline120 is null?"Complete service record":"Perfect service","service",x.ServiceDeadline120??x.FilingDate,"details");
        foreach(var x in await hearings.GetAsync(null,token))Add($"hearing-{x.Id}",x.CaseId,x.Title,"hearing",x.HearingDate,"hearings");
        return rows.OrderBy(x=>Date(x.DueDate)??DateOnly.MaxValue).ThenBy(x=>x.CaseName).Take(Math.Clamp(limit,1,200)).ToList();
    }

    private async Task<List<IssueTagRecord>> GetIssueTagsAsync(CancellationToken token)
    {
        var result=new List<IssueTagRecord>();await using var connection=connections.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();command.CommandText="SELECT id,name,description,category FROM dbo.issue_tags ORDER BY category,name";await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))result.Add(new(){Id=reader.GetInt64(0),Name=Text(reader,1)??"",Description=Text(reader,2),Category=Text(reader,3)});return result;
    }
    private async Task<List<CaseIssueTagRecord>> GetCaseIssueTagsAsync(long caseId,CancellationToken token)
    {
        var result=new List<CaseIssueTagRecord>();await using var connection=connections.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();command.CommandText="SELECT cit.id,cit.case_id,cit.issue_tag_id,it.name,it.category,it.description,cit.notes FROM dbo.case_issue_tags cit JOIN dbo.issue_tags it ON it.id=cit.issue_tag_id WHERE cit.case_id=@caseId ORDER BY it.category,it.name";command.Parameters.Add(new SqlParameter("@caseId",caseId));await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))result.Add(new(){Id=reader.GetInt64(0),CaseId=reader.GetInt64(1),IssueTagId=reader.GetInt64(2),TagName=Text(reader,3)??"",Category=Text(reader,4),Description=Text(reader,5),Notes=Text(reader,6)});return result;
    }
    private async Task<PublicationRecord?> GetPublicationAsync(long caseId,CancellationToken token)=>(await GetPublicationsAsync(caseId,token)).FirstOrDefault();
    private async Task<List<PublicationRecord>> GetPublicationsAsync(long? caseId,CancellationToken token)
    {
        var result=new List<PublicationRecord>();await using var connection=connections.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();command.CommandText="SELECT case_id,first_publication_date,second_publication_date,publication_name,marked_perfected,last_updated_at,last_updated_by,row_version FROM dbo.case_publications WHERE @caseId IS NULL OR case_id=@caseId";command.Parameters.Add(new SqlParameter("@caseId",(object?)caseId??DBNull.Value));await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))result.Add(new(){CaseId=reader.GetInt64(0),FirstPublicationDate=Text(reader,1),SecondPublicationDate=Text(reader,2),PublicationName=Text(reader,3),MarkedPerfected=Bool(reader,4),LastUpdatedAt=Text(reader,5),LastUpdatedBy=Text(reader,6),RowVersion=Convert.ToBase64String((byte[])reader.GetValue(7))});return result;
    }
    private async Task<List<DiscoveryPosture>> GetPosturesAsync(long? caseId,CancellationToken token)
    {
        var result=new List<DiscoveryPosture>();await using var connection=connections.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();command.CommandText="SELECT id,case_id,strategy,strategy_reason,strategy_selected_date,discovery_served_date,responses_due_date,responses_received_date,responses_reviewed_date,discovery_cutoff_date,planned_depositions,deficiency_status,next_decision,next_review_date,is_complete,created_at,updated_at,completion_changed_at,completion_changed_by FROM dbo.discovery_postures WHERE @caseId IS NULL OR case_id=@caseId";command.Parameters.Add(new SqlParameter("@caseId",(object?)caseId??DBNull.Value));await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))result.Add(new(){Id=reader.GetInt64(0),CaseId=reader.GetInt64(1),Strategy=Text(reader,2)??"Strategy not selected",StrategyReason=Text(reader,3),StrategySelectedDate=Text(reader,4),DiscoveryServedDate=Text(reader,5),ResponsesDueDate=Text(reader,6),ResponsesReceivedDate=Text(reader,7),ResponsesReviewedDate=Text(reader,8),DiscoveryCutoffDate=Text(reader,9),PlannedDepositions=Text(reader,10),DeficiencyStatus=Text(reader,11),NextDecision=Text(reader,12),NextReviewDate=Text(reader,13),IsComplete=Bool(reader,14),CreatedAt=Text(reader,15),UpdatedAt=Text(reader,16),CompletionChangedAt=Text(reader,17),CompletionChangedBy=Text(reader,18)});return result;
    }
    private static string? Text(DbDataReader reader,int i)=>reader.IsDBNull(i)?null:Convert.ToString(reader.GetValue(i),CultureInfo.InvariantCulture);
    private static bool Bool(DbDataReader reader,int i)=>!reader.IsDBNull(i)&&Convert.ToBoolean(reader.GetValue(i),CultureInfo.InvariantCulture);
    private static DateOnly? Date(string? value)=>DateOnly.TryParse(value,out var date)&&date!=new DateOnly(1900,1,1)?date:null;
}

internal static class DashboardComposer
{
    public static DashboardData Compose(List<CaseRecord> cases,List<DeadlineItem> deadlines,List<ChecklistItemRecord> checklist,List<DiscoveryItemRecord> discovery,List<HearingRecord> hearings,List<PublicationRecord> publications,Dictionary<long,DiscoveryPosture> postures)
    {
        var today=DateOnly.FromDateTime(DateTime.Today);var services=ServiceStatusEngine.BuildQueue(cases,publications,today);var caseMap=cases.ToDictionary(x=>x.Id);var agenda=new List<AttentionItem>();var upcoming=new List<AttentionItem>();
        void Add(List<AttentionItem> bucket,string kind,long caseId,long? id,string summary,string? due,string tab){if(caseMap.TryGetValue(caseId,out var c))bucket.Add(new(){Kind=kind,CaseId=caseId,ItemId=id,CaseName=c.CaseName,CaseNumber=c.CaseNumber,Summary=summary,DueDate=due,TargetTab=tab});}
        var stale=today.AddDays(-CaseAttentionEngine.UnconfirmedAfterDays);
        foreach(var x in deadlines.Where(x=>x.Status is not("Done" or "Complete"))){var due=Date(x.DueDate);if(due<=today&&due>=stale)Add(agenda,"deadline",x.CaseId,x.Id,x.Title,x.DueDate,"deadlines");else if(due>today&&due<=today.AddDays(30))Add(upcoming,"deadline",x.CaseId,x.Id,x.Title,x.DueDate,"deadlines");}
        foreach(var x in checklist.Where(x=>x.Status is not("Done" or "Complete" or "N/A"))){var due=Date(x.DueDate);if(due<=today)Add(agenda,"checklist",x.CaseId,x.Id,x.Task,x.DueDate,"checklist");else if(due<=today.AddDays(30))Add(upcoming,"checklist",x.CaseId,x.Id,x.Task,x.DueDate,"checklist");}
        foreach(var x in discovery.Where(x=>x.Status.Contains("Follow-Up",StringComparison.OrdinalIgnoreCase)||x.Status.Contains("Waiting",StringComparison.OrdinalIgnoreCase))){var due=x.FollowUpDate??x.DueDate;Add(Date(due)>today?upcoming:agenda,"discovery",x.CaseId,x.Id,x.RequestTitle??$"{x.Direction} {x.DiscoveryType}",due,"discovery");}
        foreach(var x in services.Where(x=>x.WarningLevel=="missing"))Add(agenda,"service",x.CaseId,null,x.WarningText,x.ServiceDeadline120,"details");
        foreach(var x in cases.Where(x=>Date(x.TrialDate) is { } d&&d>today&&d<=today.AddDays(120)))Add(upcoming,"trial",x.Id,null,"Trial / hearing date",x.TrialDate,"overview");
        var active=cases.Where(x=>x.Status is not("Closed" or "Complete" or "Triage")).ToList();var dg=deadlines.GroupBy(x=>x.CaseId).ToDictionary(x=>x.Key,x=>(IReadOnlyList<DeadlineItem>)x.ToList());var cg=checklist.GroupBy(x=>x.CaseId).ToDictionary(x=>x.Key,x=>(IReadOnlyList<ChecklistItemRecord>)x.ToList());var xg=discovery.GroupBy(x=>x.CaseId).ToDictionary(x=>x.Key,x=>(IReadOnlyList<DiscoveryItemRecord>)x.ToList());var hg=hearings.GroupBy(x=>x.CaseId).ToDictionary(x=>x.Key,x=>(IReadOnlyList<HearingRecord>)x.ToList());var sg=services.ToDictionary(x=>x.CaseId);
        var triage=active.Select(c=>DashboardTriageEngine.Evaluate(c,dg.GetValueOrDefault(c.Id,[]),cg.GetValueOrDefault(c.Id,[]),xg.GetValueOrDefault(c.Id,[]),sg.GetValueOrDefault(c.Id),hg.GetValueOrDefault(c.Id,[]),today,postures.GetValueOrDefault(c.Id)?.IsComplete==true)).Where(x=>x is not null).Select(x=>x!).OrderBy(x=>x.PriorityScore).ThenBy(x=>Date(x.DueDate)??DateOnly.MaxValue).ToList();
        return new(){OverdueDeadlines=deadlines.Count(x=>x.Status is not("Done" or "Complete")&&Date(x.DueDate)<today),DueIn7Days=deadlines.Count(x=>x.Status is not("Done" or "Complete")&&Date(x.DueDate)>=today&&Date(x.DueDate)<=today.AddDays(7)),DueIn30Days=deadlines.Count(x=>x.Status is not("Done" or "Complete")&&Date(x.DueDate)>=today&&Date(x.DueDate)<=today.AddDays(30)),UpcomingTrials=cases.Count(x=>Date(x.TrialDate)>=today&&Date(x.TrialDate)<=today.AddDays(120)),DiscoveryDue=discovery.Count(x=>x.Status.Contains("Waiting",StringComparison.OrdinalIgnoreCase)&&Date(x.DueDate)<=today.AddDays(7)),DiscoveryFollowUps=discovery.Count(x=>x.Status.Contains("Follow-Up",StringComparison.OrdinalIgnoreCase)),ChecklistDueSoon=checklist.Count(x=>x.Status is not("Done" or "Complete" or "N/A")&&Date(x.DueDate)<=today.AddDays(7)),PublicationWarnings=publications.Count(x=>!x.MarkedPerfected),ServiceDueSoon=services.Count(x=>x.WarningLevel is "urgent" or "upcoming"),ServiceOverdue=services.Count(x=>x.WarningLevel=="overdue"),CasesWithoutPerfectedService=services.Count(x=>x.ServiceRequired&&!x.ServicePerfected),MissingServiceDeadline=services.Count(x=>x.WarningLevel=="missing"),CasesNeedingReview=cases.Count(x=>string.IsNullOrWhiteSpace(x.NextAction)),ActiveCaseCount=active.Count,CasesUrgentCount=active.Count(x=>x.AttentionStatus=="urgent"),CasesAttentionCount=active.Count(x=>x.AttentionStatus=="attention"),CasesUnconfirmedCount=active.Count(x=>x.AttentionStatus=="unconfirmed"),CasesStalledCount=active.Count(x=>x.AttentionStatus=="stalled"),CasesOnTrackCount=active.Count(x=>x.AttentionStatus=="onTrack"),AttentionCases=active.Where(x=>x.AttentionStatus is "urgent" or "attention" or "stalled").OrderBy(x=>x.AttentionStatus=="urgent"?0:x.AttentionStatus=="attention"?1:2).Take(15).ToList(),TodaysAgenda=agenda.OrderBy(x=>Date(x.DueDate)??DateOnly.MaxValue).Take(20).ToList(),UpcomingDates=upcoming.OrderBy(x=>Date(x.DueDate)??today.AddDays(365)).Take(20).ToList(),TriageQueue=triage,NeedsActionNowCount=triage.Count(x=>x.MatchedCategories.Contains("needsActionNow")),ServiceRiskCount=triage.Count(x=>x.MatchedCategories.Contains("serviceRisk")),HardDeadlinesSoonCount=triage.Count(x=>x.MatchedCategories.Contains("hardDeadlinesSoon")),CourtEventsSoonCount=triage.Count(x=>x.MatchedCategories.Contains("courtEventsSoon")),BlockedCount=triage.Count(x=>x.MatchedCategories.Contains("blocked")),StaleReviewCount=triage.Count(x=>x.MatchedCategories.Contains("staleReview"))};
    }
    private static DateOnly? Date(string? value)=>DateOnly.TryParse(value,out var date)&&date!=new DateOnly(1900,1,1)?date:null;
}
