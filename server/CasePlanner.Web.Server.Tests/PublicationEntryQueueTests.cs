using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Tests;

// Covers GetPublicationEntriesAsync(null) - the bulk-mode read backing the new
// GET /api/work-queues/publication-entries endpoint (Legal Assistant Dashboard audit Phase 5),
// which needs every case's publication entries in one call to flag proof-filing exceptions across
// the docket rather than one request per case.
public sealed class PublicationEntryQueueTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task<CaseRecord> CreateCaseAsync(string name) => await _fixture.Repository.SaveCaseAsync(new CaseRecord
    {
        CaseName = name,
        County = "Pulaski",
        Status = "Active",
        CaseStatus = "Filed / Service Pending",
        Track = "Contested",
    });

    [Fact]
    public async Task GetPublicationEntriesAsync_NullCaseId_ReturnsEntriesAcrossEveryCase()
    {
        var caseA = await CreateCaseAsync("Publication Case A");
        var caseB = await CreateCaseAsync("Publication Case B");

        await _fixture.Repository.SavePublicationEntryAsync(new PublicationEntryRecord
        {
            CaseId = caseA.Id, PublicationNumber = "1", ProofFiled = false,
        });
        await _fixture.Repository.SavePublicationEntryAsync(new PublicationEntryRecord
        {
            CaseId = caseB.Id, PublicationNumber = "1", ProofFiled = true, ProofFiledDate = "2026-07-01",
        });

        var all = await _fixture.Repository.GetPublicationEntriesAsync(null);
        Assert.Contains(all, e => e.CaseId == caseA.Id && !e.ProofFiled);
        Assert.Contains(all, e => e.CaseId == caseB.Id && e.ProofFiled);
    }

    [Fact]
    public async Task GetPublicationEntriesAsync_WithCaseId_ScopesToOneCase()
    {
        var caseA = await CreateCaseAsync("Publication Case A");
        var caseB = await CreateCaseAsync("Publication Case B");
        await _fixture.Repository.SavePublicationEntryAsync(new PublicationEntryRecord { CaseId = caseA.Id, PublicationNumber = "1" });
        await _fixture.Repository.SavePublicationEntryAsync(new PublicationEntryRecord { CaseId = caseB.Id, PublicationNumber = "1" });

        var scoped = await _fixture.Repository.GetPublicationEntriesAsync(caseA.Id);
        Assert.Single(scoped);
        Assert.Equal(caseA.Id, scoped[0].CaseId);
    }
}
