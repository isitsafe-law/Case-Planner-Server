using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Tests;

public sealed class CaseCatalogPagingTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;
    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task SqliteCaseCatalogPageUsesDatabaseLimitOffsetAndReportsTotal()
    {
        var full = await _fixture.Repository.GetCasesAsync("", "", "", "", true);
        var page = await _fixture.Repository.GetCasesPageAsync("", "", "", "", true, limit: 2, offset: 1);

        Assert.Equal(full.Count, page.Total);
        Assert.Equal(2, page.Limit);
        Assert.Equal(1, page.Offset);
        Assert.Equal(full.Skip(1).Take(2).Select(item => item.Id), page.Items.Select(item => item.Id));
    }
}
