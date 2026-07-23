using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Tests;

// County Tax Collector reference lookup - same architecture as Circuit Clerk/Assessor
// (CircuitClerkTests.cs/AssessorTests.cs): a fixed, independent reference table with zero
// auth/identity dependency, seeded with real data (source: portal.arkansas.gov - no independent
// state-level collector directory exists). This file only exercises the SQLite path; the SQL
// Server migration (055_collectors.sql) has been reviewed for consistency with its siblings but
// not exercised against a live SQL Server here, same limitation already noted for every other
// migration in this repo.
public sealed class CollectorTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;
    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task FreshDatabaseIsSeededWithAllSeventyFiveCounties()
    {
        var collectors = await _fixture.Repository.GetCollectorsAsync();

        Assert.Equal(75, collectors.Count);
        Assert.Equal(collectors.Count, collectors.Select(c => c.County).Distinct().Count());
    }

    [Fact]
    public async Task ReInitializingDoesNotDuplicateSeedData()
    {
        await _fixture.Repository.InitializeAsync();
        await _fixture.Repository.InitializeAsync();

        var collectors = await _fixture.Repository.GetCollectorsAsync();
        Assert.Equal(75, collectors.Count);
    }

    [Theory]
    [InlineData("Lafayette")]
    [InlineData("Searcy")]
    public async Task NoNamePublishedCountiesHaveBlankNameAndAVerifyNote(string county)
    {
        var collectors = await _fixture.Repository.GetCollectorsAsync();

        var row = Assert.Single(collectors, c => c.County == county);
        Assert.Null(row.Name);
        Assert.NotNull(row.Address);
        Assert.NotNull(row.Phone);
        Assert.NotNull(row.Notes);
        Assert.Contains("verify with the county directly", row.Notes);
    }

    [Fact]
    public async Task SaveCollectorEditsAnExistingSeededRowByCountyWithoutCreatingADuplicate()
    {
        var before = await _fixture.Repository.GetCollectorsAsync();
        var pulaski = before.Single(c => c.County == "Pulaski");

        var updated = await _fixture.Repository.SaveCollectorAsync(new CollectorRecord
        {
            County = "Pulaski",
            Name = "New Collector Name",
            Address = pulaski.Address,
            Phone = "501-000-0000",
        });

        Assert.Equal(pulaski.Id, updated.Id);

        var reloaded = await _fixture.Repository.GetCollectorsAsync();
        Assert.Equal(75, reloaded.Count);
        var match = Assert.Single(reloaded, c => c.County == "Pulaski");
        Assert.Equal("New Collector Name", match.Name);
        Assert.Equal("501-000-0000", match.Phone);
    }

    [Fact]
    public async Task SaveCollectorCanFillInANamePreviouslyBlank()
    {
        var before = await _fixture.Repository.GetCollectorsAsync();
        var lafayette = before.Single(c => c.County == "Lafayette");
        Assert.Null(lafayette.Name);

        var updated = await _fixture.Repository.SaveCollectorAsync(new CollectorRecord
        {
            Id = lafayette.Id,
            County = lafayette.County,
            Name = "Newly Confirmed Collector",
            Address = lafayette.Address,
            Phone = lafayette.Phone,
            Notes = lafayette.Notes,
        });

        Assert.Equal("Newly Confirmed Collector", updated.Name);

        var reloaded = (await _fixture.Repository.GetCollectorsAsync()).Single(c => c.County == "Lafayette");
        Assert.Equal("Newly Confirmed Collector", reloaded.Name);
    }

    [Fact]
    public async Task SaveCollectorRoundTripsNotesAndCanClearThem()
    {
        var before = await _fixture.Repository.GetCollectorsAsync();
        var yell = before.Single(c => c.County == "Yell");

        var withNotes = await _fixture.Repository.SaveCollectorAsync(new CollectorRecord
        {
            Id = yell.Id,
            County = yell.County,
            Name = yell.Name,
            Address = yell.Address,
            Phone = yell.Phone,
            Notes = "Office moved in 2026",
        });
        Assert.Equal("Office moved in 2026", withNotes.Notes);

        var reloadedWithNotes = (await _fixture.Repository.GetCollectorsAsync()).Single(c => c.County == "Yell");
        Assert.Equal("Office moved in 2026", reloadedWithNotes.Notes);

        withNotes.Notes = null;
        var cleared = await _fixture.Repository.SaveCollectorAsync(withNotes);
        Assert.Null(cleared.Notes);

        var reloadedCleared = (await _fixture.Repository.GetCollectorsAsync()).Single(c => c.County == "Yell");
        Assert.Null(reloadedCleared.Notes);
    }
}
