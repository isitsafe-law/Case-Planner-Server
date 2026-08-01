using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Tests;

public sealed class ReportDefinitionTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;
    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task SavedReportDefinitionRoundTripsAndCanBeUpdatedOrDeleted()
    {
        var saved = await _fixture.Repository.SaveReportDefinitionAsync(new SaveReportDefinitionRequest
        {
            Name = "Upcoming trials",
            Status = "Trial Preparation",
            County = "Baxter",
            District = "3",
            Search = "sample",
            DateOpenedFrom = "2026-01-01",
            DateOpenedTo = "2026-12-31",
            Columns = ["caseName", "trialDate"],
            SortColumn = "trialDate",
            SortDirection = "desc",
        });

        var loaded = Assert.Single(await _fixture.Repository.GetSavedReportDefinitionsAsync());
        Assert.Equal(saved.Id, loaded.Id);
        Assert.Equal("Upcoming trials", loaded.Name);
        Assert.Equal(["caseName", "trialDate"], loaded.Columns);
        Assert.Equal("desc", loaded.SortDirection);

        var updated = await _fixture.Repository.SaveReportDefinitionAsync(new SaveReportDefinitionRequest
        {
            Id = saved.Id,
            Name = "Upcoming jury trials",
            Columns = ["caseNumber"],
        });
        Assert.Equal(saved.Id, updated.Id);
        Assert.Equal("Upcoming jury trials", Assert.Single(await _fixture.Repository.GetSavedReportDefinitionsAsync()).Name);

        Assert.True(await _fixture.Repository.DeleteReportDefinitionAsync(saved.Id));
        Assert.Empty(await _fixture.Repository.GetSavedReportDefinitionsAsync());
        Assert.False(await _fixture.Repository.DeleteReportDefinitionAsync(saved.Id));
    }

    [Fact]
    public async Task SavedReportDefinitionRequiresNameAndColumn()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _fixture.Repository.SaveReportDefinitionAsync(new SaveReportDefinitionRequest { Columns = ["caseName"] }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _fixture.Repository.SaveReportDefinitionAsync(new SaveReportDefinitionRequest { Name = "Empty" }));
    }
}
