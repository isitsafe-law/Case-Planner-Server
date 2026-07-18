using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Tests;

public sealed class AssignmentFilteredDashboardTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;
    public async Task InitializeAsync()=>_fixture=await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync()=>await _fixture.DisposeAsync();

    [Fact]
    public async Task DashboardAggregatesOnlyAllowedCases()
    {
        var visible=await CreateCaseAsync("Visible Assigned Case","AUTH-1");
        _=await CreateCaseAsync("Hidden Unassigned Case","AUTH-2");
        var allowed=new HashSet<long>{visible.Id};

        var dashboard=await _fixture.Repository.GetDashboardAsync(allowed);

        Assert.Equal(1,dashboard.ActiveCaseCount);
        Assert.All(dashboard.AttentionCases,x=>Assert.Equal(visible.Id,x.Id));
        Assert.All(dashboard.TodaysAgenda,x=>Assert.Equal(visible.Id,x.CaseId));
        Assert.All(dashboard.UpcomingDates,x=>Assert.Equal(visible.Id,x.CaseId));
        Assert.All(dashboard.TriageQueue,x=>Assert.Equal(visible.Id,x.CaseId));
    }

    [Fact]
    public async Task AttorneyUpcomingAndServiceViewsOnlyUseAllowedCases()
    {
        var visible=await CreateCaseAsync("Visible Assigned Case","AUTH-3");
        _=await CreateCaseAsync("Hidden Unassigned Case","AUTH-4");
        var allowed=new HashSet<long>{visible.Id};

        var attorney=await _fixture.Repository.GetAttorneyDashboardAsync(new AttorneyDashboardFilters(),allowed);
        var upcoming=await _fixture.Repository.GetUpcomingWorkAsync("all","All Open",10,allowed);
        var service=await _fixture.Repository.GetServiceQueueAsync(allowed);

        Assert.Equal(1,attorney.DocketSummary.FiledMatters+attorney.DocketSummary.PreFilingMatters);
        Assert.All(attorney.ActionQueue,x=>Assert.Equal(visible.Id,x.CaseId));
        Assert.All(attorney.MomentumReview,x=>Assert.Equal(visible.Id,x.CaseId));
        Assert.All(upcoming,x=>Assert.Equal(visible.Id,x.CaseId));
        Assert.All(service,x=>Assert.Equal(visible.Id,x.CaseId));
    }

    [Fact]
    public async Task ChildRecordResolverMapsIdsBackToOwningCase()
    {
        var owner=await CreateCaseAsync("Resolver Owner","AUTH-5");
        var note=await _fixture.Repository.SaveCaseNoteAsync(new CaseNoteRecord{CaseId=owner.Id,Title="Resolver note",Body="Body"});
        var deadline=await _fixture.Repository.SaveDeadlineAsync(new DeadlineItem{CaseId=owner.Id,Title="Resolver deadline",Status="Open"});

        Assert.Equal(owner.Id,await _fixture.Repository.GetChildCaseIdAsync("case-note",note.Id));
        Assert.Equal(owner.Id,await _fixture.Repository.GetChildCaseIdAsync("deadline",deadline.Id));
        Assert.Null(await _fixture.Repository.GetChildCaseIdAsync("case-note",long.MaxValue));
    }

    private Task<CaseRecord>CreateCaseAsync(string name,string number)=>_fixture.Repository.SaveCaseAsync(new CaseRecord
    {
        CaseName=name,CaseNumber=number,County="Pulaski",Status="Active",CaseStatus="Active Litigation",Stage="Discovery & Evaluation",Track="Contested"
    });
}
