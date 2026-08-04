using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Tests;

// Legal Assistant view, phase 2 coverage: owner_role classifies which dashboard's queue a task
// shows up in (Attorney | LegalAssistant | Either), distinct from AssignedStaffName (who among
// possibly-several people of that role currently owns it). One shared checklist_items table serves
// both dashboards through this filter rather than a second checklist system.
public sealed class ChecklistItemOwnerRoleTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;
    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task<CaseRecord> CreateCaseAsync() =>
        await _fixture.Repository.SaveCaseAsync(new CaseRecord { CaseName = "Owner Role Fixture Case", County = "Pulaski", Status = "Active", Track = "Contested" });

    [Fact]
    public async Task SaveAsync_WithoutOwnerRole_DefaultsToEither()
    {
        var c = await CreateCaseAsync();
        var saved = await _fixture.Repository.SaveChecklistItemAsync(new ChecklistItemRecord { CaseId = c.Id, Task = "Untyped task", Phase = "General", Status = "Not Started" });
        Assert.Equal("Either", saved.OwnerRole);

        var reloaded = (await _fixture.Repository.GetChecklistItemsAsync(c.Id)).Single(x => x.Id == saved.Id);
        Assert.Equal("Either", reloaded.OwnerRole);
    }

    [Theory]
    [InlineData("Attorney")]
    [InlineData("LegalAssistant")]
    [InlineData("Either")]
    public async Task SaveAsync_RoundTripsExplicitOwnerRole(string ownerRole)
    {
        var c = await CreateCaseAsync();
        var saved = await _fixture.Repository.SaveChecklistItemAsync(new ChecklistItemRecord { CaseId = c.Id, Task = "Classified task", Phase = "General", Status = "Not Started", OwnerRole = ownerRole });
        Assert.Equal(ownerRole, saved.OwnerRole);

        var reloaded = (await _fixture.Repository.GetChecklistItemsAsync(c.Id)).Single(x => x.Id == saved.Id);
        Assert.Equal(ownerRole, reloaded.OwnerRole);
    }

    [Fact]
    public async Task SaveAsync_UpdatingExistingItem_CanChangeOwnerRole()
    {
        var c = await CreateCaseAsync();
        var saved = await _fixture.Repository.SaveChecklistItemAsync(new ChecklistItemRecord { CaseId = c.Id, Task = "Reclassified task", Phase = "General", Status = "Not Started", OwnerRole = "Attorney" });
        Assert.Equal("Attorney", saved.OwnerRole);

        saved.OwnerRole = "LegalAssistant";
        var updated = await _fixture.Repository.SaveChecklistItemAsync(saved);
        Assert.Equal("LegalAssistant", updated.OwnerRole);

        var reloaded = (await _fixture.Repository.GetChecklistItemsAsync(c.Id)).Single(x => x.Id == saved.Id);
        Assert.Equal("LegalAssistant", reloaded.OwnerRole);
    }
}
