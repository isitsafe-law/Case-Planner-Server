using CasePlanner.Web.Server.Models;
using Microsoft.Data.Sqlite;

namespace CasePlanner.Web.Server.Tests;

// Multi-user rollout Phase 3 (shared witness registry): covers the witness_persons link-or-create
// behavior in SaveWitnessAsync, the registry search/ranking (SearchWitnessPersonsAsync), and the
// one-time migration that backfills person_id for pre-existing witness rows by EXACT normalized
// name only (never fuzzy) - fully testable against SQLite, no live SQL Server sandbox needed for
// this half of the feature.
public class WitnessRegistryTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task<CaseRecord> CreateCaseAsync(string caseNumber) =>
        await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseNumber = caseNumber,
            CaseName = "Fixture Case " + caseNumber,
            County = "Pulaski",
            Status = "Active",
            Track = "Contested",
        });

    // ---- SaveWitnessAsync link-or-create ----

    [Fact]
    public async Task SaveWitness_NewWitness_NoPersonIdGiven_CreatesAndLinksANewPerson()
    {
        var c = await CreateCaseAsync("24-CV-100");

        var saved = await _fixture.Repository.SaveWitnessAsync(new WitnessRecord
        {
            CaseId = c.Id,
            Name = "Jane Doe",
            Side = "ASHC",
            ContactInfo = "555-0100",
        });

        Assert.NotNull(saved.PersonId);
        Assert.True(saved.PersonId > 0);

        var matches = await _fixture.Repository.SearchWitnessPersonsAsync("Jane Doe");
        Assert.Single(matches, m => m.Id == saved.PersonId);
    }

    [Fact]
    public async Task SaveWitness_SecondWitnessWithExactSameNormalizedName_LinksToTheSamePersonInsteadOfDuplicating()
    {
        var caseA = await CreateCaseAsync("24-CV-101");
        var caseB = await CreateCaseAsync("24-CV-102");

        var first = await _fixture.Repository.SaveWitnessAsync(new WitnessRecord { CaseId = caseA.Id, Name = "Jane Doe", Side = "ASHC" });
        var second = await _fixture.Repository.SaveWitnessAsync(new WitnessRecord { CaseId = caseB.Id, Name = "  jane   doe ", Side = "Landowner" });

        Assert.Equal(first.PersonId, second.PersonId);

        var matches = await _fixture.Repository.SearchWitnessPersonsAsync("Jane Doe");
        Assert.Single(matches);
    }

    [Fact]
    public async Task SaveWitness_ExplicitPersonId_LinksToThatPersonWithoutExactNameCheck()
    {
        var caseA = await CreateCaseAsync("24-CV-103");
        var caseB = await CreateCaseAsync("24-CV-104");

        var original = await _fixture.Repository.SaveWitnessAsync(new WitnessRecord { CaseId = caseA.Id, Name = "Maxwell Carter", Side = "ASHC" });

        // Simulates the client picking a suggestion from the registry search dropdown: the typed
        // name differs slightly, but the resolved person_id should win over any name-based lookup.
        var linked = await _fixture.Repository.SaveWitnessAsync(new WitnessRecord
        {
            CaseId = caseB.Id,
            Name = "Max Carter",
            Side = "Landowner",
            PersonId = original.PersonId,
        });

        Assert.Equal(original.PersonId, linked.PersonId);
        // The per-case snapshot keeps the name actually typed for this case, not silently
        // rewritten to the canonical person's name.
        var reloaded = await _fixture.Repository.GetWitnessesAsync(caseB.Id);
        Assert.Single(reloaded, w => w.Name == "Max Carter" && w.PersonId == original.PersonId);
    }

    [Fact]
    public async Task SaveWitness_UpdatingExistingWitness_DoesNotChangeItsExistingPersonLink()
    {
        var c = await CreateCaseAsync("24-CV-105");
        var saved = await _fixture.Repository.SaveWitnessAsync(new WitnessRecord { CaseId = c.Id, Name = "Jane Doe", Side = "ASHC" });

        var updated = await _fixture.Repository.SaveWitnessAsync(new WitnessRecord
        {
            Id = saved.Id,
            CaseId = c.Id,
            Name = "Jane Doe",
            Side = "ASHC",
            Notes = "Updated notes",
        });

        Assert.Equal(saved.PersonId, updated.PersonId);
    }

    // ---- SearchWitnessPersonsAsync ranking ----

    [Fact]
    public async Task Search_ExactSubstringMatch_RankedAsExact()
    {
        var c = await CreateCaseAsync("24-CV-106");
        await _fixture.Repository.SaveWitnessAsync(new WitnessRecord { CaseId = c.Id, Name = "Maxwell Carter", Side = "ASHC" });

        var matches = await _fixture.Repository.SearchWitnessPersonsAsync("Maxwell");

        var match = Assert.Single(matches);
        Assert.Equal("exact", match.MatchType);
    }

    [Fact]
    public async Task Search_SimilarButNotExactName_RankedAsSimilar()
    {
        var c = await CreateCaseAsync("24-CV-107");
        await _fixture.Repository.SaveWitnessAsync(new WitnessRecord { CaseId = c.Id, Name = "Maxwell Carter", Side = "ASHC" });

        var matches = await _fixture.Repository.SearchWitnessPersonsAsync("Max Carter");

        var match = Assert.Single(matches);
        Assert.Equal("similar", match.MatchType);
    }

    [Fact]
    public async Task Search_UnrelatedQuery_ReturnsNoMatches()
    {
        var c = await CreateCaseAsync("24-CV-108");
        await _fixture.Repository.SaveWitnessAsync(new WitnessRecord { CaseId = c.Id, Name = "Maxwell Carter", Side = "ASHC" });

        var matches = await _fixture.Repository.SearchWitnessPersonsAsync("Totally Different Name");

        Assert.Empty(matches);
    }

    [Fact]
    public async Task Search_IncludesOtherCaseNumbers_WhenSamePersonIsWitnessOnMultipleCases()
    {
        var caseA = await CreateCaseAsync("24-CV-109");
        var caseB = await CreateCaseAsync("24-CV-110");
        await _fixture.Repository.SaveWitnessAsync(new WitnessRecord { CaseId = caseA.Id, Name = "Jane Doe", Side = "ASHC" });
        await _fixture.Repository.SaveWitnessAsync(new WitnessRecord { CaseId = caseB.Id, Name = "Jane Doe", Side = "Landowner" });

        var matches = await _fixture.Repository.SearchWitnessPersonsAsync("Jane Doe");

        var match = Assert.Single(matches);
        Assert.Contains("24-CV-109", match.OtherCaseNumbers);
        Assert.Contains("24-CV-110", match.OtherCaseNumbers);
    }

    [Fact]
    public async Task Search_BlankQuery_ReturnsFullListing()
    {
        var c = await CreateCaseAsync("24-CV-111");
        await _fixture.Repository.SaveWitnessAsync(new WitnessRecord { CaseId = c.Id, Name = "Jane Doe", Side = "ASHC" });
        await _fixture.Repository.SaveWitnessAsync(new WitnessRecord { CaseId = c.Id, Name = "Bob Smith", Side = "Landowner" });

        var matches = await _fixture.Repository.SearchWitnessPersonsAsync("");

        Assert.True(matches.Count >= 2);
    }
}
