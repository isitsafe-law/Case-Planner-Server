using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Tests;

// Build-plan step 5: Issue Tags admin (create/rename/retire + "which templates use this tag").
public sealed class IssueTagAdminTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;
    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task RenameUpdatesNameDescriptionAndCategory()
    {
        var created = await _fixture.Repository.CreateIssueTagAsync("Sign", "old description", "Property Feature");

        var renamed = await _fixture.Repository.RenameIssueTagAsync(created.Id, "Billboard", "new description", "Feature");

        Assert.Equal("Billboard", renamed.Name);
        var catalog = await _fixture.Repository.GetIssueTagsAsync();
        Assert.Contains(catalog, t => t.Id == created.Id && t.Name == "Billboard" && t.Description == "new description");
        Assert.DoesNotContain(catalog, t => t.Name == "Sign");
    }

    [Fact]
    public async Task RenameToAnExistingNameIsRejected()
    {
        await _fixture.Repository.CreateIssueTagAsync("Sign", null, null);
        var second = await _fixture.Repository.CreateIssueTagAsync("Billboard", null, null);

        await Assert.ThrowsAsync<DuplicateIssueTagException>(() => _fixture.Repository.RenameIssueTagAsync(second.Id, "Sign", null, null));
    }

    [Fact]
    public async Task RetiredTagDisappearsFromTheCatalogButItsNameCanBeReused()
    {
        var created = await _fixture.Repository.CreateIssueTagAsync("Sign", null, null);

        await _fixture.Repository.RetireIssueTagAsync(created.Id);

        var catalog = await _fixture.Repository.GetIssueTagsAsync();
        Assert.DoesNotContain(catalog, t => t.Id == created.Id);

        // Reusing the retired name should succeed, not collide with the (now-hidden) old row.
        var recreated = await _fixture.Repository.CreateIssueTagAsync("Sign", "brand new", null);
        Assert.NotEqual(created.Id, recreated.Id);
    }

    [Fact]
    public async Task RetiringAnUnknownTagThrows()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _fixture.Repository.RetireIssueTagAsync(999_999));
    }

    [Fact]
    public async Task UsageReflectsWhichTemplatesReferenceEachTag()
    {
        // The seed template's Drainage section already ties to the real "Drainage" tag.
        var usage = await _fixture.Repository.GetIssueTagUsageAsync();

        var drainage = usage.Single(u => u.TagName == "Drainage");
        Assert.Contains("Interrogatories & Requests for Production (Unified Platform)", drainage.TemplateTitles);
    }
}
