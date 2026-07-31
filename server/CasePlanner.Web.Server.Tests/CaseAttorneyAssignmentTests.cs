using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Tests;

public sealed class CaseAttorneyAssignmentTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;
    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task SupportingAttorneyAssignmentRoundTripsWithoutChangingPrimaryProjection()
    {
        var caseRecord = await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Assignment Case",
            CaseNumber = "ASSIGNMENT-1",
            County = "Pulaski",
            AssignedAttorney = "Primary Attorney",
        });

        var saved = await _fixture.Repository.SaveCaseAttorneyAssignmentAsync(new CaseAttorneyAssignmentRecord
        {
            CaseId = caseRecord.Id,
            Name = "Supporting Attorney",
            Role = "Supporting",
        });

        var assignments = await _fixture.Repository.GetCaseAttorneyAssignmentsAsync(caseRecord.Id);
        var assignment = Assert.Single(assignments, row => row.Id == saved.Id);
        Assert.Equal("Supporting", assignment.Role);
        Assert.Contains(assignments, row => row.Name == "Primary Attorney" && row.Role == "Primary");
        var reloaded = Assert.Single(await _fixture.Repository.GetCasesAsync("ASSIGNMENT-1", "", "", "", true));
        Assert.Equal("Primary Attorney", reloaded.AssignedAttorney);

        var activity = await _fixture.Repository.GetActivityLogAsync(caseRecord.Id);
        Assert.Contains(activity, entry => entry.ActivityType == "AttorneyAssignmentChanged" && entry.NewValue!.Contains("Supporting Attorney", StringComparison.Ordinal));

        await _fixture.Repository.DeleteCaseAttorneyAssignmentAsync(saved.Id);
        activity = await _fixture.Repository.GetActivityLogAsync(caseRecord.Id);
        Assert.Contains(activity, entry => entry.ActivityType == "AttorneyAssignmentRemoved" && entry.PreviousValue!.Contains("Supporting Attorney", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ChangingAssignedAttorneyToSecondChairSwapsPrimaryProjection()
    {
        var caseRecord = await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Assignment Swap Case",
            CaseNumber = "ASSIGNMENT-SWAP-1",
            County = "Pulaski",
            AssignedAttorney = "First Chair",
        });

        await _fixture.Repository.SaveCaseAttorneyAssignmentAsync(new CaseAttorneyAssignmentRecord
        {
            CaseId = caseRecord.Id,
            Name = "Second Chair",
            Role = "Supporting",
        });

        caseRecord.AssignedAttorney = "Second Chair";
        await _fixture.Repository.SaveCaseAsync(caseRecord);

        var reloaded = Assert.Single(await _fixture.Repository.GetCasesAsync("ASSIGNMENT-SWAP-1", "", "", "", true));
        Assert.Equal("Second Chair", reloaded.AssignedAttorney);
        var assignments = await _fixture.Repository.GetCaseAttorneyAssignmentsAsync(caseRecord.Id);
        Assert.Contains(assignments, row => row.Name == "Second Chair" && row.Role == "Primary");
        Assert.Contains(assignments, row => row.Name == "First Chair" && row.Role == "Supporting");
    }

    [Fact]
    public async Task ChangingAssignedAttorneyToNewAttorneyDoesNotCreateUnexpectedSecondChair()
    {
        var caseRecord = await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Assignment Replace Case",
            CaseNumber = "ASSIGNMENT-REPLACE-1",
            County = "Pulaski",
            AssignedAttorney = "First Chair",
        });

        caseRecord.AssignedAttorney = "New Primary";
        await _fixture.Repository.SaveCaseAsync(caseRecord);

        var assignments = await _fixture.Repository.GetCaseAttorneyAssignmentsAsync(caseRecord.Id);
        var primary = Assert.Single(assignments);
        Assert.Equal("New Primary", primary.Name);
        Assert.Equal("Primary", primary.Role);
    }

    [Fact]
    public async Task StartupBackfillsExistingPrimaryAttorneyAsAssignment()
    {
        var sample = Assert.Single(await _fixture.Repository.GetCasesAsync("SAMPLE-CASE-004", "", "", "", true));
        var primary = Assert.Single(await _fixture.Repository.GetCaseAttorneyAssignmentsAsync(sample.Id), row => row.Role == "Primary");
        Assert.Equal(sample.AssignedAttorney, primary.Name);
    }

    [Fact]
    public async Task PrimaryAttorneyBackfillIsIdempotentAcrossStartup()
    {
        var sample = Assert.Single(await _fixture.Repository.GetCasesAsync("SAMPLE-CASE-004", "", "", "", true));
        var before = await _fixture.Repository.GetCaseAttorneyAssignmentsAsync(sample.Id);

        await _fixture.Repository.InitializeAsync();

        var after = await _fixture.Repository.GetCaseAttorneyAssignmentsAsync(sample.Id);
        Assert.Equal(before.Count, after.Count);
        Assert.Single(after, row => row.Role == "Primary" && row.Name == sample.AssignedAttorney);
    }

    [Fact]
    public async Task DataQualityReportsDuplicateAttorneyAssignments()
    {
        var caseRecord = await _fixture.Repository.SaveCaseAsync(new CaseRecord { CaseName = "Duplicate Assignment Case", CaseNumber = "ASSIGNMENT-DQ-1", County = "Pulaski" });
        await _fixture.Repository.SaveCaseAttorneyAssignmentAsync(new CaseAttorneyAssignmentRecord { CaseId = caseRecord.Id, Name = "Same Attorney", Role = "Supporting" });
        await _fixture.Repository.SaveCaseAttorneyAssignmentAsync(new CaseAttorneyAssignmentRecord { CaseId = caseRecord.Id, Name = " same attorney ", Role = "Supporting" });

        var report = await _fixture.Repository.GetDataQualityReportAsync();
        var issue = Assert.Single(report.Issues, item => item.Key == "attorney-assignment-duplicate");
        Assert.True(issue.Count >= 1);
        Assert.Contains(caseRecord.Id, issue.SampleCaseIds);
        var directoryIssue = Assert.Single(report.Issues, item => item.Key == "attorney-assignment-not-in-directory");
        Assert.True(directoryIssue.Count >= 2);
        Assert.Contains(caseRecord.Id, directoryIssue.SampleCaseIds);
    }
}
