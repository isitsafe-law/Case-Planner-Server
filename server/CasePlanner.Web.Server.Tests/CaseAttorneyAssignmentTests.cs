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
        var assignment = Assert.Single(assignments);
        Assert.Equal(saved.Id, assignment.Id);
        Assert.Equal("Supporting", assignment.Role);
        var reloaded = Assert.Single(await _fixture.Repository.GetCasesAsync("ASSIGNMENT-1", "", "", "", true));
        Assert.Equal("Primary Attorney", reloaded.AssignedAttorney);

        var activity = await _fixture.Repository.GetActivityLogAsync(caseRecord.Id);
        Assert.Contains(activity, entry => entry.ActivityType == "AttorneyAssignmentChanged" && entry.NewValue!.Contains("Supporting Attorney", StringComparison.Ordinal));

        await _fixture.Repository.DeleteCaseAttorneyAssignmentAsync(saved.Id);
        activity = await _fixture.Repository.GetActivityLogAsync(caseRecord.Id);
        Assert.Contains(activity, entry => entry.ActivityType == "AttorneyAssignmentRemoved" && entry.PreviousValue!.Contains("Supporting Attorney", StringComparison.Ordinal));
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
    }
}
