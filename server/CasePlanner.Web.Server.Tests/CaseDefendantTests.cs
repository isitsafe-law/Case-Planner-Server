using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Tests;

// case_defendants child table - the multi-defendant replacement for the single case-level
// AnswerFiled/AnswerFiledDate bool. Mirrors CaseLegalAssistantTests's CRUD coverage structure (same
// sort-order/round-trip/delete/scoping shape); see that file for the sibling pattern. Also covers
// DefaultPostureCalculator's defendant-list-driven overload, which is what makes this feature
// behaviorally meaningful (not just a place to stash extra fields).
public class CaseDefendantTests : IAsyncLifetime
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
    public async Task SaveCaseDefendant_Insert_AssignsSequentialSortOrderAndPersists()
    {
        var c = await CreateCaseAsync();

        var first = await _fixture.Repository.SaveCaseDefendantAsync(new CaseDefendantRecord { CaseId = c.Id, Name = "John Smith", Address = "123 Main St, Little Rock, AR" });
        var second = await _fixture.Repository.SaveCaseDefendantAsync(new CaseDefendantRecord { CaseId = c.Id, Name = "Unknown Heirs of John Smith", ServiceMethod = "Warning Order" });

        Assert.NotEqual(0, first.Id);
        Assert.NotEqual(0, second.Id);
        Assert.Equal(0, first.SortOrder);
        Assert.Equal(1, second.SortOrder);

        var list = await _fixture.Repository.GetCaseDefendantsAsync(c.Id);
        Assert.Equal(2, list.Count);
        Assert.Equal("John Smith", list[0].Name);
        Assert.Equal("123 Main St, Little Rock, AR", list[0].Address);
        Assert.Equal("Unknown Heirs of John Smith", list[1].Name);
        Assert.Equal("Warning Order", list[1].ServiceMethod);
    }

    [Fact]
    public async Task SaveCaseDefendant_Update_EditsExistingRowWithoutAddingANewOne()
    {
        var c = await CreateCaseAsync();
        var saved = await _fixture.Repository.SaveCaseDefendantAsync(new CaseDefendantRecord { CaseId = c.Id, Name = "Original Name", Address = "Original Address" });

        await _fixture.Repository.SaveCaseDefendantAsync(new CaseDefendantRecord
        {
            Id = saved.Id,
            CaseId = c.Id,
            Name = "Corrected Name",
            Address = "Corrected Address",
            ServiceMethod = "Personal",
            ServedDate = "2026-01-05",
            AnswerFiled = true,
            AnswerFiledDate = "2026-02-01",
            Notes = "Landowner engaged on just compensation.",
            SortOrder = saved.SortOrder,
        });

        var list = await _fixture.Repository.GetCaseDefendantsAsync(c.Id);
        var only = Assert.Single(list);
        Assert.Equal("Corrected Name", only.Name);
        Assert.Equal("Corrected Address", only.Address);
        Assert.Equal("Personal", only.ServiceMethod);
        Assert.Equal("2026-01-05", only.ServedDate);
        Assert.True(only.AnswerFiled);
        Assert.Equal("2026-02-01", only.AnswerFiledDate);
        Assert.Equal("Landowner engaged on just compensation.", only.Notes);
    }

    [Fact]
    public async Task DeleteCaseDefendant_RemovesOnlyThatRow()
    {
        var c = await CreateCaseAsync();
        var first = await _fixture.Repository.SaveCaseDefendantAsync(new CaseDefendantRecord { CaseId = c.Id, Name = "Keep Me" });
        var second = await _fixture.Repository.SaveCaseDefendantAsync(new CaseDefendantRecord { CaseId = c.Id, Name = "Remove Me" });

        await _fixture.Repository.DeleteCaseDefendantAsync(second.Id);

        var list = await _fixture.Repository.GetCaseDefendantsAsync(c.Id);
        var only = Assert.Single(list);
        Assert.Equal(first.Id, only.Id);
        Assert.Equal("Keep Me", only.Name);
    }

    [Fact]
    public async Task GetCaseDefendants_ScopedPerCase_DoesNotLeakAcrossCases()
    {
        var caseA = await CreateCaseAsync();
        var caseB = await CreateCaseAsync();
        await _fixture.Repository.SaveCaseDefendantAsync(new CaseDefendantRecord { CaseId = caseA.Id, Name = "Defendant A" });
        await _fixture.Repository.SaveCaseDefendantAsync(new CaseDefendantRecord { CaseId = caseB.Id, Name = "Defendant B" });

        var listA = await _fixture.Repository.GetCaseDefendantsAsync(caseA.Id);
        var listB = await _fixture.Repository.GetCaseDefendantsAsync(caseB.Id);

        Assert.Single(listA, x => x.Name == "Defendant A");
        Assert.Single(listB, x => x.Name == "Defendant B");
    }

    [Fact]
    public async Task GetCaseWorkspace_IncludesCaseDefendants()
    {
        var c = await CreateCaseAsync();
        await _fixture.Repository.SaveCaseDefendantAsync(new CaseDefendantRecord { CaseId = c.Id, Name = "Workspace Defendant" });

        var workspace = await _fixture.Repository.GetCaseWorkspaceAsync(c.Id);

        Assert.NotNull(workspace);
        Assert.Single(workspace!.CaseDefendants, x => x.Name == "Workspace Defendant");
    }

    // --- Derived-warning predicate (DefaultPostureCalculator) ---

    [Fact]
    public void IsLikelyDefault_WithDefendants_TrueWhenServedDefendantHasNotAnsweredPastThreshold()
    {
        var today = new DateOnly(2026, 7, 23);
        var perfected = today.AddDays(-200).ToString("yyyy-MM-dd");
        var defendants = new List<CaseDefendantRecord>
        {
            new() { Name = "Primary Landowner", Address = "123 Main St", AnswerFiled = true },
            new() { Name = "Distant Heir", Address = "456 Elm St", AnswerFiled = false },
        };

        Assert.True(DefaultPostureCalculator.IsLikelyDefault(defendants, perfected, today));
    }

    [Fact]
    public void IsLikelyDefault_WithDefendants_FalseWhenAllServedDefendantsHaveAnswered()
    {
        var today = new DateOnly(2026, 7, 23);
        var perfected = today.AddDays(-200).ToString("yyyy-MM-dd");
        var defendants = new List<CaseDefendantRecord>
        {
            new() { Name = "Primary Landowner", Address = "123 Main St", AnswerFiled = true },
            new() { Name = "Co-Owner", Address = "789 Oak St", AnswerFiled = true },
        };

        Assert.False(DefaultPostureCalculator.IsLikelyDefault(defendants, perfected, today));
    }

    [Fact]
    public void IsLikelyDefault_WithDefendants_IgnoresWarningOrderOnlyEntriesWithNoAddress()
    {
        var today = new DateOnly(2026, 7, 23);
        var perfected = today.AddDays(-200).ToString("yyyy-MM-dd");
        var defendants = new List<CaseDefendantRecord>
        {
            new() { Name = "Unknown Heirs", ServiceMethod = "Warning Order", Address = null, AnswerFiled = false },
        };

        // No one was actually served (Warning-Order-only, no address) - nothing to be silent about.
        Assert.False(DefaultPostureCalculator.IsLikelyDefault(defendants, perfected, today));
    }

    [Fact]
    public void IsLikelyDefault_WithDefendants_FalseWhenThresholdNotYetElapsed()
    {
        var today = new DateOnly(2026, 7, 23);
        var perfected = today.AddDays(-30).ToString("yyyy-MM-dd");
        var defendants = new List<CaseDefendantRecord>
        {
            new() { Name = "Distant Heir", Address = "456 Elm St", AnswerFiled = false },
        };

        Assert.False(DefaultPostureCalculator.IsLikelyDefault(defendants, perfected, today));
    }

    [Fact]
    public async Task ApplyCaseAttention_NoDefendants_FallsBackToLegacyCaseLevelAnswerFiled()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var perfected = today.AddDays(-200).ToString("yyyy-MM-dd");
        var c = await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Legacy Case",
            County = "Pulaski",
            Status = "Active",
            Track = "Contested",
            ServicePerfected = true,
            ServicePerfectedDate = perfected,
            AnswerFiled = false,
        });

        var cases = await _fixture.Repository.GetCasesAsync("", "", "", "", true);
        var found = cases.Single(x => x.Id == c.Id);

        // No defendant rows exist for this case - the legacy single-bool AnswerFiled fact still
        // drives DefaultPostureWarning unchanged.
        Assert.True(found.DefaultPostureWarning);
    }
}
