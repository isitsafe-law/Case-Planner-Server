using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Persistence;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Tests;

public sealed class ReportExportQueryServiceTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;
    private ReportExportQueryService _queries = null!;

    public async Task InitializeAsync()
    {
        _fixture = await RepositoryTestFixture.CreateAsync();
        _queries = new ReportExportQueryService(new SqliteCaseCatalogReader(_fixture.Repository), new SqliteHearingStore(_fixture.Repository), new SqliteCaseAttorneyAssignmentStore(_fixture.Repository));
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task CaseListExportReappliesFiltersAndVisibleScope()
    {
        var included = await _fixture.Repository.SaveCaseAsync(new CaseRecord { CaseName = "Included", CaseNumber = "CASE-1", County = "Baxter", District = "District 2", CaseStatus = "Active Litigation", Status = "Active", DateOpened = "2026-07-01" });
        _ = await _fixture.Repository.SaveCaseAsync(new CaseRecord { CaseName = "Hidden", CaseNumber = "CASE-2", County = "Baxter", District = "District 2", CaseStatus = "Active Litigation", Status = "Active", DateOpened = "2026-07-01" });
        var request = new ReportExcelRequest { ReportId = "case-list", Filters = new() { ["status"] = "Active Litigation", ["county"] = "Baxter", ["district"] = "District 2", ["dateOpenedFrom"] = "2026-07-01", ["dateOpenedTo"] = "2026-07-31", ["search"] = "included" }, Columns = [new() { Key = "caseName" }, new() { Key = "caseNumber" }] };

        var rows = await _queries.GetCaseListRowsAsync(request, new HashSet<long> { included.Id }, CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal("Included", row["caseName"]);
        Assert.Equal("CASE-1", row["caseNumber"]);
    }

    [Fact]
    public async Task UpcomingTrialsUsesJuryTrialEventsAndSupportingAttorneyFilter()
    {
        var record = await _fixture.Repository.SaveCaseAsync(new CaseRecord { CaseName = "Trial Case", CaseNumber = "TRIAL-1", County = "Pulaski", Division = "North", CaseStatus = "Active Litigation", Status = "Active", AssignedAttorney = "Primary" });
        await _fixture.Repository.SaveCaseAttorneyAssignmentAsync(new CaseAttorneyAssignmentRecord { CaseId = record.Id, Name = "Second Chair", Role = "Supporting" });
        await _fixture.Repository.SaveHearingAsync(new HearingRecord { CaseId = record.Id, Title = "Jury Trial", EventType = "Jury Trial", HearingDate = "2099-08-10", EndDate = "2099-08-12", Status = "Scheduled" });
        var request = new ReportExcelRequest { ReportId = "upcoming-trials", Filters = new() { ["horizon"] = "all upcoming", ["attorney"] = "Second Chair", ["division"] = "North" }, Columns = [new() { Key = "trialDate" }, new() { Key = "additionalAttorneys" }] };

        var rows = await _queries.GetUpcomingTrialRowsAsync(request, new HashSet<long> { record.Id }, CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal("2099-08-10 – 2099-08-12", row["trialDate"]);
        Assert.Equal("Second Chair", row["additionalAttorneys"]);
    }

    [Fact]
    public async Task OutcomeAndCycleTimeExportsApplyTheirEligibilityRules()
    {
        var eligible = await _fixture.Repository.SaveCaseAsync(new CaseRecord { CaseName = "Eligible", CaseNumber = "ELIGIBLE", CaseStatus = "Resolved / Closed", Status = "Closed", DepositAmount = 100, FinalJudgmentAmount = 125, FilingDate = "2026-01-01", ClosedDate = "2026-02-01" });
        var incomplete = await _fixture.Repository.SaveCaseAsync(new CaseRecord { CaseName = "Incomplete", CaseNumber = "INCOMPLETE", CaseStatus = "Resolved / Closed", Status = "Closed", DepositAmount = 100, ClosedDate = "2026-02-01" });
        var outcomeRequest = new ReportExcelRequest { ReportId = "outcomes", Columns = [new() { Key = "caseName" }, new() { Key = "ratio" }] };
        var cycleRequest = new ReportExcelRequest { ReportId = "cycle-time", Columns = [new() { Key = "caseName" }, new() { Key = "days" }] };

        var outcomeRows = await _queries.GetOutcomeRowsAsync(outcomeRequest, new HashSet<long> { eligible.Id, incomplete.Id }, CancellationToken.None);
        var cycleRows = await _queries.GetCycleTimeRowsAsync(cycleRequest, new HashSet<long> { eligible.Id, incomplete.Id }, CancellationToken.None);

        Assert.Equal("Eligible", Assert.Single(outcomeRows)["caseName"]);
        Assert.Equal("Eligible", Assert.Single(cycleRows)["caseName"]);
    }
}
