using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Tests;

// Build-plan step 3: the fixed issue-tag vocabulary was the Phase 1 audit's top complaint - no
// create-tag endpoint existed anywhere, so a new issue tag required a code change and a redeploy.
// This is the SQLite side of fixing that (CasePlannerRepository.CreateIssueTagAsync).
public sealed class IssueTagCreationTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;
    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task NewTagIsCreatedAndAppearsInTheCatalog()
    {
        var created = await _fixture.Repository.CreateIssueTagAsync("Sign", "Billboard or outdoor advertising structure", "Property Feature");

        Assert.True(created.Id > 0);
        var catalog = await _fixture.Repository.GetIssueTagsAsync();
        Assert.Contains(catalog, t => t.Name == "Sign" && t.Category == "Property Feature");
    }

    [Fact]
    public async Task DuplicateNameIsRejectedCaseInsensitively()
    {
        await _fixture.Repository.CreateIssueTagAsync("Sign", null, null);

        await Assert.ThrowsAsync<DuplicateIssueTagException>(() => _fixture.Repository.CreateIssueTagAsync("sign", null, null));
    }

    [Fact]
    public async Task BlankNameIsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _fixture.Repository.CreateIssueTagAsync("   ", null, null));
    }
}
