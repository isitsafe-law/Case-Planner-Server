using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Tests;

public sealed class AttorneyDashboardComposerTests
{
    [Fact]
    public void Compose_AppliesContentFiltersWithoutChangingSummaryCards()
    {
        var cases=new[]
        {
            new CaseRecord{Id=1,CaseName="Pulaski Matter",County="Pulaski",Status="Active",CaseStatus="Active Litigation",MatterType="FiledCase",CurrentHolder="Attorney",LastMeaningfulActivityDate="2026-07-01"},
            new CaseRecord{Id=2,CaseName="Saline Matter",County="Saline",Status="Active",CaseStatus="Pipeline",MatterType="PreFilingTract",CurrentHolder="Attorney",LastMeaningfulActivityDate="2026-07-01"}
        };
        var all=AttorneyDashboardComposer.Compose(cases,[],[],[],new(),new DateOnly(2026,7,16));
        var filtered=AttorneyDashboardComposer.Compose(cases,[],[],[],new(){County="Pulaski"},new DateOnly(2026,7,16));
        Assert.Equal(all.SummaryCounts.OnMyDesk,filtered.SummaryCounts.OnMyDesk);
        Assert.Equal(2,all.SummaryCounts.OnMyDesk);
        Assert.Single(filtered.MomentumReview);
        Assert.Empty(filtered.FilingPipeline.AllPipeline);
    }

    [Fact]
    public void Compose_SeparatesPipelineAndFiledMatters()
    {
        var cases=new[]
        {
            new CaseRecord{Id=1,CaseName="Filed",Status="Active",CaseStatus="Active Litigation",MatterType="FiledCase",LastMeaningfulActivityDate="2026-07-01"},
            new CaseRecord{Id=2,CaseName="Pipeline",Status="Active",CaseStatus="Pipeline",MatterType="PreFilingTract",CurrentHolder="Attorney"}
        };
        var result=AttorneyDashboardComposer.Compose(cases,[],[],[],new(),new DateOnly(2026,7,16));
        Assert.Equal(1,result.DocketSummary.FiledMatters);
        Assert.Equal(1,result.DocketSummary.PreFilingMatters);
        Assert.Single(result.FilingPipeline.AllPipeline);
        Assert.Single(result.MomentumReview);
    }

    [Fact]
    public void Compose_TrialWatchEntryShowsLatestOfferInsteadOfRemovedSettlementAuthority()
    {
        var cases=new[]
        {
            new CaseRecord{Id=1,CaseName="Trial Track Matter",Status="Active",CaseStatus="Trial Preparation",MatterType="FiledCase",TrialTrack=true,LastMeaningfulActivityDate="2026-07-01"},
        };
        var lastOffersByCase=new Dictionary<long,decimal?>{[1]=185000m};
        var result=AttorneyDashboardComposer.Compose(cases,[],[],[],new(),new DateOnly(2026,7,16),lastOffersByCase: lastOffersByCase);
        var entry=Assert.Single(result.TrialWatch);
        Assert.Equal(185000m,entry.LastOffer);
    }

    // ROW-intake tracts sent back to ROW or at a terminal outcome drop out of the dashboard's
    // active set - same exclusion already applied for Closed/Complete/Triage - while still being
    // fully queryable elsewhere (the general case list, direct search).
    [Fact]
    public void Compose_ExcludesRowIntakeInactiveTracts_ButKeepsLiveRoundsOnTheDesk()
    {
        var cases=new[]
        {
            new CaseRecord{Id=1,CaseName="In Title Review Tract",Status="Active",CaseStatus="Pipeline",MatterType="PreFilingTract",CurrentHolder="Attorney",RowIntakeStatus="In Title Review"},
            new CaseRecord{Id=2,CaseName="Returned To ROW Tract",Status="Active",CaseStatus="Pipeline",MatterType="PreFilingTract",CurrentHolder="Attorney",RowIntakeStatus="Returned to ROW"},
            new CaseRecord{Id=3,CaseName="Acquired Tract",Status="Active",CaseStatus="Pipeline",MatterType="PreFilingTract",CurrentHolder="Attorney",RowIntakeStatus="Acquired by Agreement"},
        };
        var result=AttorneyDashboardComposer.Compose(cases,[],[],[],new(),new DateOnly(2026,7,16));
        Assert.Equal(1,result.SummaryCounts.OnMyDesk);
        var row=Assert.Single(result.FilingPipeline.AllPipeline);
        Assert.Equal(1,row.CaseId);
    }
}
