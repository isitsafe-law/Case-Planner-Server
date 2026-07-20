using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Tests;

// SaveCaseInternalAsync (CasePlannerRepository.cs) auto-fills ServiceDeadline120 from the filing
// date (or an explicit ServiceDeadlineBasisDate, if set) the first time it's blank - on every
// save, including brand-new case creation. This is intentional per the code comment above that
// block, not something ServiceStatusEngineTests exercises (that suite only covers the live/derived
// computation, not the persisted auto-fill-on-save behavior). This test confirms the persisted
// path is genuinely wired up end to end through SaveCaseAsync.
public sealed class ServiceDeadlineAutoPopulateTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;
    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task NewCaseWithOnlyFilingDateGetsServiceDeadline120SetTo120DaysLater()
    {
        var saved = await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Auto Service Deadline Case",
            CaseNumber = "SVC-AUTO-1",
            County = "Pulaski",
            Status = "Active",
            CaseStatus = "Active Litigation",
            Stage = "Discovery & Evaluation",
            Track = "Contested",
            FilingDate = "2026-01-01",
        });

        Assert.Equal("2026-05-01", saved.ServiceDeadline120);

        var reloaded = await _fixture.Repository.GetCasesAsync("", "", "", "", true);
        var match = Assert.Single(reloaded, c => c.Id == saved.Id);
        Assert.Equal("2026-05-01", match.ServiceDeadline120);
    }
}
