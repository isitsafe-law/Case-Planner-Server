using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Tests;

// Newspaper of general circulation reference lookup (final implementation, item 7). Unlike
// Circuit Clerk/Assessor/Collector, this is NOT one-row-per-county: a county can have multiple
// newspapers, there is no seed data, and rows are true per-row CRUD keyed by Id.
public sealed class NewspaperTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;
    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task FreshDatabaseHasNoNewspapersAndNoSeedData()
    {
        var newspapers = await _fixture.Repository.GetNewspapersAsync();
        Assert.Empty(newspapers);
    }

    [Fact]
    public async Task SaveNewspaperWithIdZeroCreatesANewRow()
    {
        var created = await _fixture.Repository.SaveNewspaperAsync(new NewspaperRecord
        {
            County = "Pulaski",
            Name = "Arkansas Democrat-Gazette",
            IsGeneralCirculation = true,
        });

        Assert.True(created.Id > 0);
        var all = await _fixture.Repository.GetNewspapersAsync();
        var match = Assert.Single(all);
        Assert.Equal("Arkansas Democrat-Gazette", match.Name);
        Assert.True(match.IsActive);
    }

    [Fact]
    public async Task ACountyCanHaveMultipleNewspapersAsDistinctRows()
    {
        await _fixture.Repository.SaveNewspaperAsync(new NewspaperRecord { County = "Pulaski", Name = "Paper One" });
        await _fixture.Repository.SaveNewspaperAsync(new NewspaperRecord { County = "Pulaski", Name = "Paper Two" });

        var all = await _fixture.Repository.GetNewspapersAsync();
        Assert.Equal(2, all.Count);
        Assert.Equal(2, all.Select(n => n.Id).Distinct().Count());
        Assert.All(all, n => Assert.Equal("Pulaski", n.County));
    }

    [Fact]
    public async Task SavingWithANonzeroIdUpdatesThatRowInPlaceWithoutCreatingADuplicate()
    {
        var created = await _fixture.Repository.SaveNewspaperAsync(new NewspaperRecord { County = "Yell", Name = "Original Name" });

        created.Name = "Updated Name";
        created.TypicalCost = 42.50m;
        var updated = await _fixture.Repository.SaveNewspaperAsync(created);

        Assert.Equal(created.Id, updated.Id);
        var all = await _fixture.Repository.GetNewspapersAsync();
        var match = Assert.Single(all);
        Assert.Equal("Updated Name", match.Name);
        Assert.Equal(42.50m, match.TypicalCost);
    }

    [Fact]
    public async Task IsActiveIsASoftDisableFlagNotADelete()
    {
        var created = await _fixture.Repository.SaveNewspaperAsync(new NewspaperRecord { County = "Boone", Name = "Boone Paper" });

        created.IsActive = false;
        await _fixture.Repository.SaveNewspaperAsync(created);

        var all = await _fixture.Repository.GetNewspapersAsync();
        var match = Assert.Single(all);
        Assert.False(match.IsActive);
    }
}
