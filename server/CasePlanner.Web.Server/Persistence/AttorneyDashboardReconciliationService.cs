using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

public sealed record AttorneyDashboardMismatch(string Field,string SqliteValue,string SqlServerValue);
public sealed record AttorneyDashboardReconciliation(bool Matches,List<AttorneyDashboardMismatch> Mismatches);

public sealed class AttorneyDashboardReconciliationService(CasePlannerRepository sqlite,SqlServerWorkspaceQuery sql)
{
    public async Task<AttorneyDashboardReconciliation> CompareAsync(AttorneyDashboardFilters filters,CancellationToken token=default)
    {
        var a=await sqlite.GetAttorneyDashboardAsync(filters);var b=await sql.GetAttorneyDashboardAsync(filters,null,token);var mismatches=new List<AttorneyDashboardMismatch>();
        C("Summary.NeedsJudgment",a.SummaryCounts.NeedsJudgment,b.SummaryCounts.NeedsJudgment,mismatches);
        C("Summary.Stalled",a.SummaryCounts.Stalled,b.SummaryCounts.Stalled,mismatches);
        C("Summary.DiscoveryUnset",a.SummaryCounts.DiscoveryUnset,b.SummaryCounts.DiscoveryUnset,mismatches);
        C("Summary.OnMyDesk",a.SummaryCounts.OnMyDesk,b.SummaryCounts.OnMyDesk,mismatches);
        C("Summary.TrialTrack",a.SummaryCounts.TrialTrack,b.SummaryCounts.TrialTrack,mismatches);
        C("Summary.MissingNextReview",a.SummaryCounts.MissingNextReview,b.SummaryCounts.MissingNextReview,mismatches);
        C("ActionQueue",Ids(a.ActionQueue.Select(x=>x.CaseId)),Ids(b.ActionQueue.Select(x=>x.CaseId)),mismatches);
        C("MomentumReview",Ids(a.MomentumReview.Select(x=>x.CaseId)),Ids(b.MomentumReview.Select(x=>x.CaseId)),mismatches);
        C("Pipeline.MyDesk",Ids(a.FilingPipeline.MyDesk.Select(x=>x.CaseId)),Ids(b.FilingPipeline.MyDesk.Select(x=>x.CaseId)),mismatches);
        C("Pipeline.Waiting",Ids(a.FilingPipeline.Waiting.Select(x=>x.CaseId)),Ids(b.FilingPipeline.Waiting.Select(x=>x.CaseId)),mismatches);
        C("Pipeline.All",Ids(a.FilingPipeline.AllPipeline.Select(x=>x.CaseId)),Ids(b.FilingPipeline.AllPipeline.Select(x=>x.CaseId)),mismatches);
        C("TrialWatch",Ids(a.TrialWatch.Select(x=>x.CaseId)),Ids(b.TrialWatch.Select(x=>x.CaseId)),mismatches);
        C("UpcomingDecisions",Ids(a.UpcomingDecisions.Select(x=>x.CaseId)),Ids(b.UpcomingDecisions.Select(x=>x.CaseId)),mismatches);
        C("ProjectWatch",string.Join("|",a.ProjectWatch.Select(x=>x.ProjectName)),string.Join("|",b.ProjectWatch.Select(x=>x.ProjectName)),mismatches);
        C("DiscoveryControl",Discovery(a.DiscoveryControl),Discovery(b.DiscoveryControl),mismatches);
        C("DocketSummary",Docket(a.DocketSummary),Docket(b.DocketSummary),mismatches);
        C("TriageCaseCount",a.TriageCaseCount,b.TriageCaseCount,mismatches);
        return new(mismatches.Count==0,mismatches);
    }
    private static string Ids(IEnumerable<long> ids)=>string.Join(",",ids);
    private static string Discovery(DiscoveryControlSummary x)=>$"{x.StrategyNotSelected},{x.StrategySelectedNotServed},{x.ResponsesOverdue},{x.ResponsesReceivedNotReviewed},{x.DeficienciesUnresolved},{x.DepositionDecisionPending},{x.CutoffApproaching},{x.Complete},{x.NoDiscoveryNeeded}";
    private static string Docket(AttorneyDocketSummary x)=>$"{x.PreFilingMatters},{x.FiledMatters},{x.TrialTrackMatters},{x.WaitingAppropriately},{x.OnAttorneysDesk},{x.MissingNextReviewDate}";
    private static void C(string field,object a,object b,List<AttorneyDashboardMismatch> result){var x=Convert.ToString(a)??"";var y=Convert.ToString(b)??"";if(x!=y)result.Add(new(field,x,y));}
}
