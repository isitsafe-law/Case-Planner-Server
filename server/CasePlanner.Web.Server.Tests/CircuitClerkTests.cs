using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Tests;

// Circuit Clerk reference lookup - same architecture as Staff Directory (StaffDirectoryTests.cs):
// a fixed, independent reference table with zero auth/identity dependency, seeded with real data
// (source: arcourts.gov's official Arkansas Judiciary circuit clerks directory). This file only
// exercises the SQLite path; the SQL Server migration (053_circuit_clerks.sql) has been reviewed
// for consistency with its siblings but not exercised against a live SQL Server here, same
// limitation already noted for every other migration in this repo.
public sealed class CircuitClerkTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;
    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task FreshDatabaseIsSeededWithAllSeventyFiveCounties()
    {
        var clerks = await _fixture.Repository.GetCircuitClerksAsync();

        Assert.Equal(75, clerks.Count);
        // Every county name must be unique - Carroll's two offices are combined into one row.
        Assert.Equal(clerks.Count, clerks.Select(c => c.County).Distinct().Count());
        Assert.All(clerks, c => Assert.False(string.IsNullOrWhiteSpace(c.ClerkName)));
    }

    [Fact]
    public async Task CarrollCountyCombinesBothOfficesIntoOneRowWithTwoAddressLines()
    {
        var clerks = await _fixture.Repository.GetCircuitClerksAsync();

        var carroll = Assert.Single(clerks, c => c.County == "Carroll");
        Assert.Equal("Sara Huffman", carroll.ClerkName);
        Assert.NotNull(carroll.Address);
        Assert.Contains("Berryville", carroll.Address);
        Assert.Contains("Eureka Springs", carroll.Address);
        // Two lines, one field - matches CaseDefendantRecord.Address's multi-address convention.
        Assert.Contains('\n', carroll.Address);
    }

    [Fact]
    public async Task ReInitializingDoesNotDuplicateSeedData()
    {
        await _fixture.Repository.InitializeAsync();
        await _fixture.Repository.InitializeAsync();

        var clerks = await _fixture.Repository.GetCircuitClerksAsync();
        Assert.Equal(75, clerks.Count);
    }

    [Fact]
    public async Task SaveCircuitClerkEditsAnExistingSeededRowByCountyWithoutCreatingADuplicate()
    {
        var before = await _fixture.Repository.GetCircuitClerksAsync();
        var pulaski = before.Single(c => c.County == "Pulaski");

        // Simulate a Settings-panel edit keyed by county (Id unknown to the caller ahead of time,
        // like the PUT /api/circuit-clerks/{county} endpoint does before calling SaveAsync).
        var updated = await _fixture.Repository.SaveCircuitClerkAsync(new CircuitClerkRecord
        {
            County = "Pulaski",
            ClerkName = "New Clerk Name",
            Address = pulaski.Address,
            Phone = "501-000-0000",
        });

        Assert.Equal(pulaski.Id, updated.Id);

        var reloaded = await _fixture.Repository.GetCircuitClerksAsync();
        Assert.Equal(75, reloaded.Count);
        var match = Assert.Single(reloaded, c => c.County == "Pulaski");
        Assert.Equal("New Clerk Name", match.ClerkName);
        Assert.Equal("501-000-0000", match.Phone);
    }

    [Fact]
    public async Task SaveCircuitClerkRoundTripsNotesAndCanClearThem()
    {
        var before = await _fixture.Repository.GetCircuitClerksAsync();
        var yell = before.Single(c => c.County == "Yell");

        var withNotes = await _fixture.Repository.SaveCircuitClerkAsync(new CircuitClerkRecord
        {
            Id = yell.Id,
            County = yell.County,
            ClerkName = yell.ClerkName,
            Address = yell.Address,
            Phone = yell.Phone,
            Notes = "Office moved in 2026",
        });
        Assert.Equal("Office moved in 2026", withNotes.Notes);

        var reloadedWithNotes = (await _fixture.Repository.GetCircuitClerksAsync()).Single(c => c.County == "Yell");
        Assert.Equal("Office moved in 2026", reloadedWithNotes.Notes);

        withNotes.Notes = null;
        var cleared = await _fixture.Repository.SaveCircuitClerkAsync(withNotes);
        Assert.Null(cleared.Notes);

        var reloadedCleared = (await _fixture.Repository.GetCircuitClerksAsync()).Single(c => c.County == "Yell");
        Assert.Null(reloadedCleared.Notes);
    }
}
