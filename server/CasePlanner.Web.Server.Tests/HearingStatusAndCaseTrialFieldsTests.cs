using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Tests;

// Schema-additions pass (Batch 2): hearings.status, cases.trial_end_date, cases.property_description.
// These are plain plumbing fields with zero doc-gen token dependency - these tests just confirm
// each one round-trips through SaveXAsync/GetXAsync, matching the style of AssignmentFilteredDashboardTests'
// CreateCaseAsync helper and IssueTagCreationTests' fixture usage.
public sealed class HearingStatusAndCaseTrialFieldsTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;
    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task HearingStatusDefaultsToScheduledAndRoundTrips()
    {
        var owner = await CreateCaseAsync("Hearing Status Owner", "HEARING-STATUS-1");

        var created = await _fixture.Repository.SaveHearingAsync(new HearingRecord
        {
            CaseId = owner.Id,
            Title = "Motion Hearing",
            HearingDate = "2026-08-01",
        });

        Assert.Equal("Scheduled", created.Status);

        var updated = await _fixture.Repository.SaveHearingAsync(new HearingRecord
        {
            Id = created.Id,
            CaseId = owner.Id,
            Title = "Motion Hearing",
            HearingDate = "2026-08-01",
            Status = "Continued",
        });

        Assert.Equal("Continued", updated.Status);

        var reloaded = await _fixture.Repository.GetHearingsAsync(owner.Id);
        Assert.Contains(reloaded, h => h.Id == created.Id && h.Status == "Continued");
    }

    [Fact]
    public async Task CaseTrialEndDateAndPropertyDescriptionRoundTrip()
    {
        var saved = await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Trial Range Case",
            CaseNumber = "TRIAL-RANGE-1",
            County = "Pulaski",
            Status = "Active",
            CaseStatus = "Active Litigation",
            Stage = "Trial Track",
            Track = "Contested",
            TrialDate = "2026-09-01",
            TrialEndDate = "2026-09-05",
            PropertyDescription = "40-acre tract fronting Highway 10.",
        });

        Assert.Equal("2026-09-05", saved.TrialEndDate);
        Assert.Equal("40-acre tract fronting Highway 10.", saved.PropertyDescription);

        var reloaded = await _fixture.Repository.GetCasesAsync("", "", "", "", true);
        var match = Assert.Single(reloaded, c => c.Id == saved.Id);
        Assert.Equal("2026-09-05", match.TrialEndDate);
        Assert.Equal("40-acre tract fronting Highway 10.", match.PropertyDescription);
    }

    private Task<CaseRecord> CreateCaseAsync(string name, string number) => _fixture.Repository.SaveCaseAsync(new CaseRecord
    {
        CaseName = name,
        CaseNumber = number,
        County = "Pulaski",
        Status = "Active",
        CaseStatus = "Active Litigation",
        Stage = "Discovery & Evaluation",
        Track = "Contested",
    });
}
