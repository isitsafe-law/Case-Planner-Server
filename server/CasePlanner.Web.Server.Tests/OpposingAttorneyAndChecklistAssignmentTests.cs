using CasePlanner.Web.Server.Models;
using Microsoft.Data.Sqlite;

namespace CasePlanner.Web.Server.Tests;

// Multi-user rollout Phase 2, item 1: case.opposing_counsel (a single free-text string with no
// document-generation coupling anywhere) converted to a one-to-many case_opposing_attorneys child
// table. Covers the new CRUD directly against SQLite (fully testable here - no live SQL Server
// caveat applies to this half) and the one-time data-preservation migration that copies any
// existing non-blank opposing_counsel value into the new table.
public class OpposingAttorneyAndChecklistAssignmentTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task<CaseRecord> CreateCaseAsync(string? opposingCounsel = null) =>
        await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Fixture Case",
            County = "Pulaski",
            Status = "Active",
            Track = "Contested",
            OpposingCounsel = opposingCounsel,
        });

    [Fact]
    public async Task SaveOpposingAttorney_Insert_AssignsSequentialSortOrderAndPersists()
    {
        var c = await CreateCaseAsync();

        var first = await _fixture.Repository.SaveOpposingAttorneyAsync(new OpposingAttorneyRecord { CaseId = c.Id, Name = "Jane Smith" });
        var second = await _fixture.Repository.SaveOpposingAttorneyAsync(new OpposingAttorneyRecord { CaseId = c.Id, Name = "Bob Jones" });

        Assert.NotEqual(0, first.Id);
        Assert.NotEqual(0, second.Id);
        Assert.Equal(0, first.SortOrder);
        Assert.Equal(1, second.SortOrder);

        var list = await _fixture.Repository.GetOpposingAttorneysAsync(c.Id);
        Assert.Equal(2, list.Count);
        Assert.Equal("Jane Smith", list[0].Name);
        Assert.Equal("Bob Jones", list[1].Name);
    }

    [Fact]
    public async Task SaveOpposingAttorney_Update_RenamesExistingRowWithoutAddingANewOne()
    {
        var c = await CreateCaseAsync();
        var saved = await _fixture.Repository.SaveOpposingAttorneyAsync(new OpposingAttorneyRecord { CaseId = c.Id, Name = "Original Name" });

        await _fixture.Repository.SaveOpposingAttorneyAsync(new OpposingAttorneyRecord { Id = saved.Id, CaseId = c.Id, Name = "Corrected Name", SortOrder = saved.SortOrder });

        var list = await _fixture.Repository.GetOpposingAttorneysAsync(c.Id);
        var only = Assert.Single(list);
        Assert.Equal("Corrected Name", only.Name);
    }

    [Fact]
    public async Task DeleteOpposingAttorney_RemovesOnlyThatRow()
    {
        var c = await CreateCaseAsync();
        var first = await _fixture.Repository.SaveOpposingAttorneyAsync(new OpposingAttorneyRecord { CaseId = c.Id, Name = "Keep Me" });
        var second = await _fixture.Repository.SaveOpposingAttorneyAsync(new OpposingAttorneyRecord { CaseId = c.Id, Name = "Remove Me" });

        await _fixture.Repository.DeleteOpposingAttorneyAsync(second.Id);

        var list = await _fixture.Repository.GetOpposingAttorneysAsync(c.Id);
        var only = Assert.Single(list);
        Assert.Equal(first.Id, only.Id);
        Assert.Equal("Keep Me", only.Name);
    }

    [Fact]
    public async Task GetOpposingAttorneys_ScopedPerCase_DoesNotLeakAcrossCases()
    {
        var caseA = await CreateCaseAsync();
        var caseB = await CreateCaseAsync();
        await _fixture.Repository.SaveOpposingAttorneyAsync(new OpposingAttorneyRecord { CaseId = caseA.Id, Name = "Attorney A" });
        await _fixture.Repository.SaveOpposingAttorneyAsync(new OpposingAttorneyRecord { CaseId = caseB.Id, Name = "Attorney B" });

        var listA = await _fixture.Repository.GetOpposingAttorneysAsync(caseA.Id);
        var listB = await _fixture.Repository.GetOpposingAttorneysAsync(caseB.Id);

        Assert.Single(listA, x => x.Name == "Attorney A");
        Assert.Single(listB, x => x.Name == "Attorney B");
    }

    [Fact]
    public async Task GetCaseWorkspace_IncludesOpposingAttorneys()
    {
        var c = await CreateCaseAsync();
        await _fixture.Repository.SaveOpposingAttorneyAsync(new OpposingAttorneyRecord { CaseId = c.Id, Name = "Workspace Attorney" });

        var workspace = await _fixture.Repository.GetCaseWorkspaceAsync(c.Id);

        Assert.NotNull(workspace);
        Assert.Single(workspace!.OpposingAttorneys, x => x.Name == "Workspace Attorney");
    }

    // ---- one-time opposing_counsel -> case_opposing_attorneys migration ----

    private async Task ResetMigrationFlagAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={_fixture.DatabasePath}");
        await connection.OpenAsync();
        var deleteFlag = connection.CreateCommand();
        deleteFlag.CommandText = "DELETE FROM app_settings WHERE key='opposing_counsel_migrated_v1'";
        await deleteFlag.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Migration_CopiesExistingOpposingCounselValueIntoNewTable()
    {
        var c = await CreateCaseAsync(opposingCounsel: "Legacy Opposing Firm, LLP");
        await ResetMigrationFlagAsync();

        await _fixture.Repository.InitializeAsync();

        var list = await _fixture.Repository.GetOpposingAttorneysAsync(c.Id);
        var only = Assert.Single(list);
        Assert.Equal("Legacy Opposing Firm, LLP", only.Name);

        // The old column is preserved, not dropped or cleared.
        var reloaded = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == c.Id);
        Assert.Equal("Legacy Opposing Firm, LLP", reloaded.OpposingCounsel);
    }

    [Fact]
    public async Task Migration_SkipsCasesWithBlankOpposingCounsel()
    {
        var c = await CreateCaseAsync(opposingCounsel: "   ");
        await ResetMigrationFlagAsync();

        await _fixture.Repository.InitializeAsync();

        var list = await _fixture.Repository.GetOpposingAttorneysAsync(c.Id);
        Assert.Empty(list);
    }

    [Fact]
    public async Task Migration_IsIdempotent_DoesNotDuplicateOnSecondRun()
    {
        var c = await CreateCaseAsync(opposingCounsel: "Legacy Opposing Firm, LLP");
        await ResetMigrationFlagAsync();

        await _fixture.Repository.InitializeAsync();
        await _fixture.Repository.InitializeAsync();

        var list = await _fixture.Repository.GetOpposingAttorneysAsync(c.Id);
        Assert.Single(list);
    }

    [Fact]
    public async Task Migration_DoesNotAddASecondRow_WhenCaseAlreadyHasAnOpposingAttorneyRow()
    {
        var c = await CreateCaseAsync(opposingCounsel: "Legacy Opposing Firm, LLP");
        await _fixture.Repository.SaveOpposingAttorneyAsync(new OpposingAttorneyRecord { CaseId = c.Id, Name = "Already Entered Manually" });
        await ResetMigrationFlagAsync();

        await _fixture.Repository.InitializeAsync();

        var list = await _fixture.Repository.GetOpposingAttorneysAsync(c.Id);
        var only = Assert.Single(list);
        Assert.Equal("Already Entered Manually", only.Name);
    }

    // ---- item 2: checklist_items.assigned_user_id round-trip (SQLite is an opaque passthrough
    // column here - no app_users table to validate against) ----

    [Fact]
    public async Task ChecklistItem_AssignedUserId_RoundTripsOnSaveAndRead()
    {
        var c = await CreateCaseAsync();
        var userId = Guid.NewGuid().ToString();

        var saved = await _fixture.Repository.SaveChecklistItemAsync(new ChecklistItemRecord
        {
            CaseId = c.Id,
            Phase = "General",
            Task = "Draft interrogatories",
            Status = "Not Started",
            AssignedUserId = userId,
        });

        Assert.Equal(userId, saved.AssignedUserId);

        var reloaded = await _fixture.Repository.GetChecklistItemsAsync(c.Id);
        Assert.Single(reloaded, x => x.Id == saved.Id && x.AssignedUserId == userId);
    }

    [Fact]
    public async Task ChecklistItem_AssignedUserId_CanBeClearedByUpdating()
    {
        var c = await CreateCaseAsync();
        var saved = await _fixture.Repository.SaveChecklistItemAsync(new ChecklistItemRecord
        {
            CaseId = c.Id,
            Phase = "General",
            Task = "Draft interrogatories",
            Status = "Not Started",
            AssignedUserId = Guid.NewGuid().ToString(),
        });

        var updated = await _fixture.Repository.SaveChecklistItemAsync(new ChecklistItemRecord
        {
            Id = saved.Id,
            CaseId = c.Id,
            Phase = "General",
            Task = "Draft interrogatories",
            Status = "Not Started",
            AssignedUserId = null,
        });

        Assert.Null(updated.AssignedUserId);
        var reloaded = await _fixture.Repository.GetChecklistItemsAsync(c.Id);
        Assert.Single(reloaded, x => x.Id == saved.Id && x.AssignedUserId == null);
    }
}
