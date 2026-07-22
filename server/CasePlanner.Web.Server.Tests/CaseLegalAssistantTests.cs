using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Tests;

// Test-build feedback item: case.assignedAttorney's tied legal assistant used to be derived live
// (whichever Staff Directory legal assistant lists this case's attorney) and shown as a single
// read-only value. Converted to a one-to-many case_legal_assistants child table so a case can hold
// more than one legal assistant (two attorneys on one case, each with their own LA) and support a
// manual override. Mirrors OpposingAttorneyAndChecklistAssignmentTests's CRUD coverage structure
// (same sort-order/round-trip/delete/scoping shape) - see that file for the sibling pattern.
public class CaseLegalAssistantTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task<CaseRecord> CreateCaseAsync() =>
        await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Fixture Case",
            County = "Pulaski",
            Status = "Active",
            Track = "Contested",
        });

    [Fact]
    public async Task SaveCaseLegalAssistant_Insert_AssignsSequentialSortOrderAndPersists()
    {
        var c = await CreateCaseAsync();

        var first = await _fixture.Repository.SaveCaseLegalAssistantAsync(new CaseLegalAssistantRecord { CaseId = c.Id, Name = "Tyler Story" });
        var second = await _fixture.Repository.SaveCaseLegalAssistantAsync(new CaseLegalAssistantRecord { CaseId = c.Id, Name = "Evelyn Allison" });

        Assert.NotEqual(0, first.Id);
        Assert.NotEqual(0, second.Id);
        Assert.Equal(0, first.SortOrder);
        Assert.Equal(1, second.SortOrder);

        var list = await _fixture.Repository.GetCaseLegalAssistantsAsync(c.Id);
        Assert.Equal(2, list.Count);
        Assert.Equal("Tyler Story", list[0].Name);
        Assert.Equal("Evelyn Allison", list[1].Name);
    }

    [Fact]
    public async Task SaveCaseLegalAssistant_Update_RenamesExistingRowWithoutAddingANewOne()
    {
        var c = await CreateCaseAsync();
        var saved = await _fixture.Repository.SaveCaseLegalAssistantAsync(new CaseLegalAssistantRecord { CaseId = c.Id, Name = "Original Name" });

        await _fixture.Repository.SaveCaseLegalAssistantAsync(new CaseLegalAssistantRecord { Id = saved.Id, CaseId = c.Id, Name = "Corrected Name", SortOrder = saved.SortOrder });

        var list = await _fixture.Repository.GetCaseLegalAssistantsAsync(c.Id);
        var only = Assert.Single(list);
        Assert.Equal("Corrected Name", only.Name);
    }

    [Fact]
    public async Task DeleteCaseLegalAssistant_RemovesOnlyThatRow()
    {
        var c = await CreateCaseAsync();
        var first = await _fixture.Repository.SaveCaseLegalAssistantAsync(new CaseLegalAssistantRecord { CaseId = c.Id, Name = "Keep Me" });
        var second = await _fixture.Repository.SaveCaseLegalAssistantAsync(new CaseLegalAssistantRecord { CaseId = c.Id, Name = "Remove Me" });

        await _fixture.Repository.DeleteCaseLegalAssistantAsync(second.Id);

        var list = await _fixture.Repository.GetCaseLegalAssistantsAsync(c.Id);
        var only = Assert.Single(list);
        Assert.Equal(first.Id, only.Id);
        Assert.Equal("Keep Me", only.Name);
    }

    [Fact]
    public async Task GetCaseLegalAssistants_ScopedPerCase_DoesNotLeakAcrossCases()
    {
        var caseA = await CreateCaseAsync();
        var caseB = await CreateCaseAsync();
        await _fixture.Repository.SaveCaseLegalAssistantAsync(new CaseLegalAssistantRecord { CaseId = caseA.Id, Name = "Assistant A" });
        await _fixture.Repository.SaveCaseLegalAssistantAsync(new CaseLegalAssistantRecord { CaseId = caseB.Id, Name = "Assistant B" });

        var listA = await _fixture.Repository.GetCaseLegalAssistantsAsync(caseA.Id);
        var listB = await _fixture.Repository.GetCaseLegalAssistantsAsync(caseB.Id);

        Assert.Single(listA, x => x.Name == "Assistant A");
        Assert.Single(listB, x => x.Name == "Assistant B");
    }

    [Fact]
    public async Task GetCaseLegalAssistants_SupportsMultipleAssistantsOnOneCase()
    {
        var c = await CreateCaseAsync();
        await _fixture.Repository.SaveCaseLegalAssistantAsync(new CaseLegalAssistantRecord { CaseId = c.Id, Name = "Tyler Story" });
        await _fixture.Repository.SaveCaseLegalAssistantAsync(new CaseLegalAssistantRecord { CaseId = c.Id, Name = "Evelyn Allison" });

        var list = await _fixture.Repository.GetCaseLegalAssistantsAsync(c.Id);

        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task GetCaseWorkspace_IncludesCaseLegalAssistants()
    {
        var c = await CreateCaseAsync();
        await _fixture.Repository.SaveCaseLegalAssistantAsync(new CaseLegalAssistantRecord { CaseId = c.Id, Name = "Workspace Assistant" });

        var workspace = await _fixture.Repository.GetCaseWorkspaceAsync(c.Id);

        Assert.NotNull(workspace);
        Assert.Single(workspace!.CaseLegalAssistants, x => x.Name == "Workspace Assistant");
    }
}
