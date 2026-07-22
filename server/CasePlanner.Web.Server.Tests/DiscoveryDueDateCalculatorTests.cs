using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Tests;

// DiscoveryDueDateCalculator is a small pure helper (DomainModels.cs) rather than a
// deadline_templates entry, because deadline templates compute off a single-value trigger field on
// the case row (filing_date, trial_date) but a case can have many discovery requests, each with its
// own ServedDate/DueDate (DiscoveryItemRecord) - see the workflow doc's "responses due 30 days from
// service" note, folded into the Discovery - Core checklist template's task text instead (Task G).
public sealed class DiscoveryDueDateCalculatorTests
{
    [Fact]
    public void ServedDateSetAndDueDateBlank_ComputesServedDatePlus30Days()
    {
        var result = DiscoveryDueDateCalculator.ComputeDefaultDueDate("2026-01-01", null);
        Assert.Equal("2026-01-31", result);
    }

    [Fact]
    public void ServedDateSetAndDueDateEmptyString_ComputesServedDatePlus30Days()
    {
        var result = DiscoveryDueDateCalculator.ComputeDefaultDueDate("2026-06-15", "");
        Assert.Equal("2026-07-15", result);
    }

    [Fact]
    public void DueDateAlreadySet_IsNeverOverwritten()
    {
        // Whether the existing due date is a manual override or a previously-computed default,
        // it must never be recalculated/overwritten on a later save.
        var result = DiscoveryDueDateCalculator.ComputeDefaultDueDate("2026-01-01", "2026-03-01");
        Assert.Equal("2026-03-01", result);
    }

    [Fact]
    public void NoServedDate_ReturnsDueDateUnchanged()
    {
        Assert.Null(DiscoveryDueDateCalculator.ComputeDefaultDueDate(null, null));
        Assert.Equal("", DiscoveryDueDateCalculator.ComputeDefaultDueDate(null, ""));
        Assert.Equal("2026-05-01", DiscoveryDueDateCalculator.ComputeDefaultDueDate(null, "2026-05-01"));
    }

    [Fact]
    public void UnparseableServedDate_ReturnsDueDateUnchanged()
    {
        Assert.Null(DiscoveryDueDateCalculator.ComputeDefaultDueDate("not-a-date", null));
    }
}

// Confirms the SQLite persistence path (CasePlannerRepository.SaveDiscoveryItemAsync) actually
// applies the pure calculator above before writing, end to end.
public sealed class DiscoveryItemDueDateAutoCalcIntegrationTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task<CaseRecord> CreateCaseAsync() =>
        await _fixture.Repository.SaveCaseAsync(new CaseRecord { CaseName = "Discovery Due Date Case", County = "Pulaski", Status = "Active", Track = "Contested" });

    [Fact]
    public async Task SaveDiscoveryItem_WithServedDateAndNoDueDate_AutoComputesDueDate()
    {
        var c = await CreateCaseAsync();
        var saved = await _fixture.Repository.SaveDiscoveryItemAsync(new DiscoveryItemRecord
        {
            CaseId = c.Id,
            DiscoveryType = "Interrogatories",
            ServedDate = "2026-02-01",
        });

        Assert.Equal("2026-03-03", saved.DueDate);
        var reloaded = Assert.Single(await _fixture.Repository.GetDiscoveryItemsAsync(c.Id), x => x.Id == saved.Id);
        Assert.Equal("2026-03-03", reloaded.DueDate);
    }

    [Fact]
    public async Task SaveDiscoveryItem_WithManuallySetDueDate_NeverOverwritesIt()
    {
        var c = await CreateCaseAsync();
        var saved = await _fixture.Repository.SaveDiscoveryItemAsync(new DiscoveryItemRecord
        {
            CaseId = c.Id,
            DiscoveryType = "Interrogatories",
            ServedDate = "2026-02-01",
            DueDate = "2026-02-20",
        });

        Assert.Equal("2026-02-20", saved.DueDate);
    }

    [Fact]
    public async Task SaveDiscoveryItem_WithNoServedDate_LeavesDueDateNull()
    {
        var c = await CreateCaseAsync();
        var saved = await _fixture.Repository.SaveDiscoveryItemAsync(new DiscoveryItemRecord
        {
            CaseId = c.Id,
            DiscoveryType = "Interrogatories",
        });

        Assert.Null(saved.DueDate);
    }
}
