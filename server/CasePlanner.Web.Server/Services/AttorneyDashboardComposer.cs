using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Services;

public static class AttorneyDashboardComposer
{
    private sealed record Evaluated(CaseRecord Case,bool IsPreFiling,int? DaysSince,string? Momentum,DiscoveryPosture? Posture);

    public static AttorneyDashboardResponse Compose(
        IEnumerable<CaseRecord> caseSource,IEnumerable<DeadlineItem> deadlineSource,
        IEnumerable<HearingRecord> hearingSource,IEnumerable<DiscoveryPosture> postureSource,
        AttorneyDashboardFilters filters,DateOnly? asOf=null)
    {
        var today=asOf??DateOnly.FromDateTime(DateTime.Today);
        // Database collations differ (SQLite binary ordering versus SQL Server's configured
        // collation), so establish a provider-neutral order before building UI sections.
        var allCases=caseSource.OrderBy(c=>c.CaseName,StringComparer.OrdinalIgnoreCase).ThenBy(c=>c.Id).ToList();
        var active=allCases.Where(c=>c.Status is not("Closed" or "Complete" or "Triage")).ToList();
        var triageCount=allCases.Count(c=>c.Status=="Triage");
        var deadlineGroups=deadlineSource.Where(d=>d.Status is not("Done" or "Complete"))
            .GroupBy(d=>d.CaseId).ToDictionary(g=>g.Key,g=>(IReadOnlyList<DeadlineItem>)g.ToList());
        var hearingGroups=hearingSource.GroupBy(h=>h.CaseId).ToDictionary(g=>g.Key,g=>(IReadOnlyList<HearingRecord>)g.ToList());
        var postures=postureSource.ToDictionary(p=>p.CaseId);

        var evaluated=active.Select(c=>
        {
            var pipeline=string.Equals(c.CaseStatus,"Pipeline",StringComparison.OrdinalIgnoreCase)
                ||string.Equals(c.MatterType,"PreFilingTract",StringComparison.OrdinalIgnoreCase);
            var days=AttorneyDashboardEngine.DaysSinceMeaningfulActivity(c,today);
            return new Evaluated(c,pipeline,days,pipeline?null:AttorneyDashboardEngine.EvaluateMomentumStatus(c,today,days),pipeline?null:postures.GetValueOrDefault(c.Id));
        }).ToList();

        bool Matches(Evaluated matter)
        {
            var c=matter.Case;
            if(!SameOrBlank(filters.MatterType,c.MatterType)||!SameOrBlank(filters.Project,c.ProjectName)
                ||!SameOrBlank(filters.County,c.County)||!SameOrBlank(filters.Priority,c.Priority)
                ||!SameOrBlank(filters.CurrentHolder,c.CurrentHolder))return false;
            if(!string.IsNullOrWhiteSpace(filters.Stage)&&!string.Equals(c.Stage,filters.Stage,StringComparison.OrdinalIgnoreCase)&&!string.Equals(c.PipelineStage,filters.Stage,StringComparison.OrdinalIgnoreCase))return false;
            if(filters.TrialTrack is { } track&&c.TrialTrack!=track)return false;
            if(!SameOrBlank(filters.MomentumStatus,matter.Momentum))return false;
            if(!string.IsNullOrWhiteSpace(filters.Search))
            {
                var search=filters.Search;
                if(!c.CaseName.Contains(search,StringComparison.OrdinalIgnoreCase)
                    &&!c.CaseNumber.Contains(search,StringComparison.OrdinalIgnoreCase)
                    &&!c.JobNumber.Contains(search,StringComparison.OrdinalIgnoreCase))return false;
            }
            return true;
        }

        var summary=new AttorneyDashboardSummaryCounts
        {
            NeedsJudgment=evaluated.Count(m=>!m.IsPreFiling
                ?AttorneyDashboardEngine.EvaluateFiledCase(m.Case,m.Posture,deadlineGroups.GetValueOrDefault(m.Case.Id,[]),hearingGroups.GetValueOrDefault(m.Case.Id,[]),today)?.ActionCategory=="Decide"
                :string.Equals(m.Case.CurrentHolder,"Attorney",StringComparison.OrdinalIgnoreCase)),
            Stalled=evaluated.Count(m=>m.Momentum=="Stalled"),
            DiscoveryUnset=evaluated.Count(m=>!m.IsPreFiling&&AttorneyDashboardEngine.EvaluateDiscoveryConditions(m.Posture,today).Contains("Strategy not selected")),
            OnMyDesk=evaluated.Count(m=>string.Equals(m.Case.CurrentHolder,"Attorney",StringComparison.OrdinalIgnoreCase)),
            TrialTrack=evaluated.Count(m=>m.Case.TrialTrack),
            MissingNextReview=evaluated.Count(m=>!m.IsPreFiling&&string.IsNullOrWhiteSpace(m.Case.WaitingOn)
                &&(string.IsNullOrWhiteSpace(m.Case.NextAction)||string.IsNullOrWhiteSpace(m.Case.NextReviewDate??m.Case.NextActionDue)))
        };

        var matched=evaluated.Where(Matches).ToList();
        var actions=new List<ActionQueueItem>();var momentum=new List<MomentumReviewEntry>();var discovery=new DiscoveryControlSummary();
        var myDesk=new List<PreFilingTractRow>();var waiting=new List<PreFilingTractRow>();var pipeline=new List<PreFilingTractRow>();var trials=new List<TrialWatchEntry>();
        foreach(var matter in matched)
        {
            var c=matter.Case;
            if(matter.IsPreFiling)
            {
                var bucket=AttorneyDashboardEngine.PipelineBucket(c);
                var row=new PreFilingTractRow
                {
                    CaseId=c.Id,TractOrOwnerName=string.IsNullOrWhiteSpace(c.CaseName)?c.Landowner??c.Tract:c.CaseName,
                    ProjectName=c.ProjectName,JobNumber=c.JobNumber,County=c.County,CurrentHolder=c.CurrentHolder,
                    PipelineStage=c.PipelineStage,DateSentToCurrentHolder=c.DateSentToCurrentHolder,Priority=c.Priority,
                    NextReviewDate=c.NextReviewDate,CurrentIssue=c.CurrentIssue,LastFollowUpDate=c.WaitingFollowUpDate,
                    LastUpdated=c.UpdatedAt,FlagReason=bucket=="Waiting"
                        ?AttorneyDashboardEngine.WaitingMonitorReason(c,today,matter.DaysSince)
                        :AttorneyDashboardEngine.MyDeskFlagReason(c)
                };
                pipeline.Add(row);if(bucket=="MyDesk")myDesk.Add(row);else waiting.Add(row);
                if(AttorneyDashboardEngine.PreFilingBelongsInActionQueue(c,today))actions.Add(AttorneyDashboardEngine.EvaluatePreFilingTract(c,today));
                continue;
            }

            var action=AttorneyDashboardEngine.EvaluateFiledCase(c,matter.Posture,deadlineGroups.GetValueOrDefault(c.Id,[]),hearingGroups.GetValueOrDefault(c.Id,[]),today);
            if(action is not null)actions.Add(action);
            momentum.Add(new(){CaseId=c.Id,CaseName=c.CaseName,CaseNumber=Blank(c.CaseNumber),MomentumStatus=matter.Momentum??"Moving",DaysSinceMeaningfulActivity=matter.DaysSince??0,WaitingOn=c.WaitingOn,WaitingFollowUpDate=c.WaitingFollowUpDate});
            foreach(var condition in AttorneyDashboardEngine.EvaluateDiscoveryConditions(matter.Posture,today))Increment(discovery,condition,c,matter.Posture);
            if(AttorneyDashboardEngine.IsTrialWatchEligible(c,today,AttorneyDashboardEngine.DefaultTrialWatchDays))trials.Add(Trial(c,matter.Posture,today));
        }

        actions=actions.OrderBy(a=>a.PriorityLevel).ThenBy(a=>Date(a.ReviewDate)??DateOnly.MaxValue).ThenByDescending(a=>a.DaysSinceMeaningfulActivity??0).ToList();
        return new()
        {
            SummaryCounts=summary,ActionQueue=actions,DiscoveryControl=discovery,MomentumReview=momentum,
            FilingPipeline=new(){MyDesk=myDesk,Waiting=waiting,AllPipeline=pipeline},TrialWatch=trials,
            UpcomingDecisions=actions.Where(a=>a.ActionCategory=="Decide").Select(a=>new UpcomingDecisionItem{CaseId=a.CaseId,CaseName=a.CaseName,DecisionType=a.Reason,RelevantDate=a.ReviewDate,Context=a.PostureSummary,RecommendedPreparationDate=a.ReviewDate,Status="Pending"}).ToList(),
            ProjectWatch=Projects(matched.Select(m=>m.Case),today),
            DocketSummary=new()
            {
                PreFilingMatters=matched.Count(m=>m.IsPreFiling),FiledMatters=matched.Count(m=>!m.IsPreFiling),
                TrialTrackMatters=matched.Count(m=>m.Case.TrialTrack),WaitingAppropriately=matched.Count(m=>m.Momentum=="Waiting Appropriately"),
                OnAttorneysDesk=matched.Count(m=>string.Equals(m.Case.CurrentHolder,"Attorney",StringComparison.OrdinalIgnoreCase)),
                MissingNextReviewDate=matched.Count(m=>!m.IsPreFiling&&string.IsNullOrWhiteSpace(m.Case.WaitingOn)
                    &&(string.IsNullOrWhiteSpace(m.Case.NextAction)||string.IsNullOrWhiteSpace(m.Case.NextReviewDate??m.Case.NextActionDue)))
            },
            TriageCaseCount=triageCount
        };
    }

    private static void Increment(DiscoveryControlSummary summary,string condition,CaseRecord c,DiscoveryPosture? posture)
    {
        switch(condition)
        {
            case "Strategy not selected":summary.StrategyNotSelected++;break;
            case "Strategy selected but discovery not served":summary.StrategySelectedNotServed++;break;
            case "Responses overdue":summary.ResponsesOverdue++;break;
            case "Responses received but not reviewed":summary.ResponsesReceivedNotReviewed++;break;
            case "Deficiencies unresolved":summary.DeficienciesUnresolved++;break;
            case "Deposition decision pending":summary.DepositionDecisionPending++;break;
            case "Discovery cutoff approaching":summary.CutoffApproaching++;break;
            case "Discovery complete":summary.Complete++;break;
            case "No discovery currently needed":summary.NoDiscoveryNeeded++;break;
        }
        if(!summary.CasesByCondition.TryGetValue(condition,out var list)){list=[];summary.CasesByCondition[condition]=list;}
        list.Add(new(){CaseId=c.Id,CaseName=c.CaseName,CaseNumber=Blank(c.CaseNumber),Strategy=posture?.Strategy??"Strategy not selected",NextDecision=posture?.NextDecision,NextReviewDate=posture?.NextReviewDate});
    }

    private static TrialWatchEntry Trial(CaseRecord c,DiscoveryPosture? posture,DateOnly today)=>
        new(){CaseId=c.Id,CaseName=c.CaseName,CaseNumber=Blank(c.CaseNumber),TrialDate=c.TrialDate,
            DaysUntilTrial=Date(c.TrialDate) is { } trial?trial.DayNumber-today.DayNumber:null,Deposit=c.DepositAmount,
            FeeComparisonNote=AttorneyDashboardEngine.BuildFeeComparisonNote(c.DepositAmount,null,null),
            DiscoveryStatus=posture is null?"Strategy not selected":posture.IsComplete?"Discovery complete":posture.Strategy,
            NextTrialDecision=string.IsNullOrWhiteSpace(c.NextAction)?"Confirm final valuation position and settlement recommendation":c.NextAction};

    private static List<ProjectWatchRow> Projects(IEnumerable<CaseRecord> cases,DateOnly today)
    {
        var rows=new List<ProjectWatchRow>();
        foreach(var group in cases.Where(c=>!string.IsNullOrWhiteSpace(c.ProjectName)||!string.IsNullOrWhiteSpace(c.JobNumber))
                    .GroupBy(c=>string.IsNullOrWhiteSpace(c.ProjectName)?c.JobNumber!:c.ProjectName!))
        {
            var tracts=group.ToList();if(tracts.Count<2)continue;
            var stalled=tracts.Where(c=>AttorneyDashboardEngine.EvaluateMomentumStatus(c,today,AttorneyDashboardEngine.DaysSinceMeaningfulActivity(c,today))=="Stalled").ToList();
            var appraisers=tracts.Where(c=>!string.IsNullOrWhiteSpace(c.Appraiser)).GroupBy(c=>c.Appraiser!).Where(g=>g.Count()>=2&&g.Count(stalled.Contains)>=2).ToList();
            var issue=appraisers.Count>0?$"Possible common appraiser delay: {appraisers[0].Key} across {appraisers[0].Count()} tracts":null;
            var trial=tracts.Select(c=>Date(c.TrialDate)).Where(d=>d is not null).Select(d=>d!.Value).Order().FirstOrDefault();
            var oldest=tracts.Select(c=>(c.CaseName,Days:AttorneyDashboardEngine.DaysSinceMeaningfulActivity(c,today))).Where(x=>x.Days is not null).OrderByDescending(x=>x.Days).FirstOrDefault();
            rows.Add(new(){ProjectName=group.Key,JobNumber=tracts.Select(c=>c.JobNumber).FirstOrDefault(j=>!string.IsNullOrWhiteSpace(j)),
                TractCount=tracts.Count,PreFilingCount=tracts.Count(c=>c.MatterType=="PreFilingTract"),FiledCount=tracts.Count(c=>c.MatterType!="PreFilingTract"),
                ResolvedCount=tracts.Count(c=>c.Status is "Closed" or "Complete"),OnAttorneyDeskCount=tracts.Count(c=>string.Equals(c.CurrentHolder,"Attorney",StringComparison.OrdinalIgnoreCase)),
                StalledCount=stalled.Count,EarliestTrialDate=trial==default?null:trial.ToString("yyyy-MM-dd"),OldestInactiveMatter=oldest.CaseName,SharedIssue=issue,
                NextProjectDecision=issue is null?null:"Review whether the shared appraiser delay warrants a coordinated response across tracts"});
        }
        return rows;
    }

    private static bool SameOrBlank(string? filter,string? value)=>string.IsNullOrWhiteSpace(filter)||string.Equals(filter,value,StringComparison.OrdinalIgnoreCase);
    private static string? Blank(string? value)=>string.IsNullOrWhiteSpace(value)?null:value;
    private static DateOnly? Date(string? value)=>DateOnly.TryParse(value,out var date)&&date!=new DateOnly(1900,1,1)?date:null;
}
