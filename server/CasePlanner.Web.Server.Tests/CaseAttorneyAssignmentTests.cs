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
}
