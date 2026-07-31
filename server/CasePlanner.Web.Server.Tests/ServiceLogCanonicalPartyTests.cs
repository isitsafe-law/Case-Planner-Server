using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Tests;

public sealed class ServiceLogCanonicalPartyTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;
    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task ServiceLogReferenceUsesCanonicalPartyAndPreservesSnapshot()
    {
        var caseRecord = await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Canonical Service Party Case",
            CaseNumber = "SERVICE-PARTY-1",
            County = "Pulaski",
            Status = "Active",
            CaseStatus = "Active Litigation",
            Stage = "Discovery & Evaluation",
        });
        var defendant = await _fixture.Repository.SaveCaseDefendantAsync(new CaseDefendantRecord
        {
            CaseId = caseRecord.Id,
            Name = "Jane Doe",
        });

        var saved = await _fixture.Repository.SaveServiceLogEntryAsync(new ServiceLogEntry
        {
            CaseId = caseRecord.Id,
            CaseDefendantId = defendant.Id,
            PartyName = "Older typed name",
            Status = "Attempted",
        });

        Assert.Equal(defendant.Id, saved.CaseDefendantId);
        Assert.Equal("Jane Doe", saved.PartyName);

        var reloaded = Assert.Single(await _fixture.Repository.GetServiceLogEntriesAsync(caseRecord.Id));
        Assert.Equal(defendant.Id, reloaded.CaseDefendantId);
        Assert.Equal("Jane Doe", reloaded.PartyName);
    }

    [Fact]
    public async Task ServiceLogRejectsPartyFromAnotherCase()
    {
        var first = await _fixture.Repository.SaveCaseAsync(new CaseRecord { CaseName = "First", CaseNumber = "SERVICE-PARTY-2A", County = "Pulaski" });
        var second = await _fixture.Repository.SaveCaseAsync(new CaseRecord { CaseName = "Second", CaseNumber = "SERVICE-PARTY-2B", County = "Pulaski" });
        var defendant = await _fixture.Repository.SaveCaseDefendantAsync(new CaseDefendantRecord { CaseId = first.Id, Name = "Wrong Case Party" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => _fixture.Repository.SaveServiceLogEntryAsync(new ServiceLogEntry
        {
            CaseId = second.Id,
            CaseDefendantId = defendant.Id,
            PartyName = "Wrong Case Party",
        }));
    }
}
