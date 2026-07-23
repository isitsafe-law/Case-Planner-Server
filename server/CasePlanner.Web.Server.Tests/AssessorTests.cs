using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Tests;

// County Assessor reference lookup - same architecture as Circuit Clerk (CircuitClerkTests.cs): a
// fixed, independent reference table with zero auth/identity dependency, seeded with real data
// (source: dfa.arkansas.gov's Assessment Coordination Division directory, cross-checked with
// portal.arkansas.gov). This file only exercises the SQLite path; the SQL Server migration
// (054_assessors.sql) has been reviewed for consistency with its siblings but not exercised against
// a live SQL Server here, same limitation already noted for every other migration in this repo.
public sealed class AssessorTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;
    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task FreshDatabaseIsSeededWithAllSeventyFiveCounties()
    {
        var assessors = await _fixture.Repository.GetAssessorsAsync();

        Assert.Equal(75, assessors.Count);
        Assert.Equal(assessors.Count, assessors.Select(a => a.County).Distinct().Count());
        Assert.All(assessors, a => Assert.False(string.IsNullOrWhiteSpace(a.Name)));
    }

    [Fact]
    public async Task ReInitializingDoesNotDuplicateSeedData()
    {
        await _fixture.Repository.InitializeAsync();
        await _fixture.Repository.InitializeAsync();

        var assessors = await _fixture.Repository.GetAssessorsAsync();
        Assert.Equal(75, assessors.Count);
    }

    [Fact]
    public async Task BentonCountyCombinesAllFourOfficesIntoOneRowWithFourAddressLines()
    {
        var assessors = await _fixture.Repository.GetAssessorsAsync();

        var benton = Assert.Single(assessors, a => a.County == "Benton");
        Assert.NotNull(benton.Address);
        Assert.Contains("Bentonville", benton.Address);
        Assert.Contains("Gravette", benton.Address);
        Assert.Contains("Rogers", benton.Address);
        Assert.Contains("Siloam Springs", benton.Address);
        Assert.Equal(3, benton.Address!.Count(c => c == '\n'));
    }

    [Fact]
    public async Task CalhounCountyDiscrepancyNoteRoundTripsVerbatim()
    {
        var assessors = await _fixture.Repository.GetAssessorsAsync();

        var calhoun = Assert.Single(assessors, a => a.County == "Calhoun");
        Assert.Equal("Teresa Carter", calhoun.Name);
        Assert.NotNull(calhoun.Notes);
        Assert.Contains("Teresa Ables", calhoun.Notes);
        Assert.Contains("verify before relying on this for a real notification", calhoun.Notes);
    }

    [Fact]
    public async Task SaveAssessorEditsAnExistingSeededRowByCountyWithoutCreatingADuplicate()
    {
        var before = await _fixture.Repository.GetAssessorsAsync();
        var pulaski = before.Single(a => a.County == "Pulaski");

        var updated = await _fixture.Repository.SaveAssessorAsync(new AssessorRecord
        {
            County = "Pulaski",
            Name = "New Assessor Name",
            Address = pulaski.Address,
            Phone = "501-000-0000",
        });

        Assert.Equal(pulaski.Id, updated.Id);

        var reloaded = await _fixture.Repository.GetAssessorsAsync();
        Assert.Equal(75, reloaded.Count);
        var match = Assert.Single(reloaded, a => a.County == "Pulaski");
        Assert.Equal("New Assessor Name", match.Name);
        Assert.Equal("501-000-0000", match.Phone);
    }

    [Fact]
    public async Task SaveAssessorRoundTripsNotesAndCanClearThem()
    {
        var before = await _fixture.Repository.GetAssessorsAsync();
        var yell = before.Single(a => a.County == "Yell");

        var withNotes = await _fixture.Repository.SaveAssessorAsync(new AssessorRecord
        {
            Id = yell.Id,
            County = yell.County,
            Name = yell.Name,
            Address = yell.Address,
            Phone = yell.Phone,
            Notes = "Office moved in 2026",
        });
        Assert.Equal("Office moved in 2026", withNotes.Notes);

        var reloadedWithNotes = (await _fixture.Repository.GetAssessorsAsync()).Single(a => a.County == "Yell");
        Assert.Equal("Office moved in 2026", reloadedWithNotes.Notes);

        withNotes.Notes = null;
        var cleared = await _fixture.Repository.SaveAssessorAsync(withNotes);
        Assert.Null(cleared.Notes);

        var reloadedCleared = (await _fixture.Repository.GetAssessorsAsync()).Single(a => a.County == "Yell");
        Assert.Null(reloadedCleared.Notes);
    }
}
