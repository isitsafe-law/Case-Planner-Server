using CasePlanner.Web.Server.Models;
using Microsoft.Data.Sqlite;

namespace CasePlanner.Web.Server.Tests;

// Covers the ARDOT workflow rewrite's two structural fixes to checklist_templates/deadline_templates:
// (1) is_custom - a durable "a firm has touched this" marker that must survive a reseed even when
// TemplateSeeds/DeadlineTemplateSeeds content changes and ChecklistTemplateVersion/
// DeadlineTemplateVersion bump; (2) the rewritten Stage templates actually producing the expected
// checklist content for a fresh case. Mirrors RiskAnalysisMigrationTests's pattern of resetting a
// version/flag row directly against the fixture's SQLite file, then re-running InitializeAsync() to
// force the gated logic to actually run again.
public sealed class WorkTemplateSeedingAndGenerationTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task ResetSeedVersionFlagsAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={_fixture.DatabasePath}");
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM app_settings WHERE key IN ('checklist_template_version','deadline_template_version')";
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Reseed_PreservesCustomTemplates_ButRefreshesStockOnes()
    {
        // Custom checklist template: SaveChecklistTemplateAsync always marks is_custom=1 the
        // moment it's touched, even though this one happens to share a stock template's stage.
        var customChecklist = await _fixture.Repository.SaveChecklistTemplateAsync(new ChecklistTemplateRecord
        {
            Name = "Firm-Specific Intake Add-On",
            TriggerType = "Stage",
            Stage = "Pipeline",
            Active = true,
        });
        Assert.True(customChecklist.IsCustom);

        var customDeadline = await _fixture.Repository.SaveDeadlineTemplateAsync(new DeadlineTemplateRecord
        {
            Name = "Firm-Specific Reminder",
            TriggerField = "filing_date",
            OffsetDays = 45,
            Title = "Firm-specific custom reminder",
            Severity = "soft",
            Active = true,
        });
        Assert.True(customDeadline.IsCustom);

        var stockChecklistBefore = (await _fixture.Repository.GetChecklistTemplatesAsync())
            .Single(t => t.Name == "Pre-Suit / Intake");
        Assert.False(stockChecklistBefore.IsCustom);

        await ResetSeedVersionFlagsAsync();
        await _fixture.Repository.InitializeAsync();

        // Custom rows survive the reseed completely untouched (same id, same content).
        var checklistAfter = await _fixture.Repository.GetChecklistTemplatesAsync();
        var customChecklistAfter = Assert.Single(checklistAfter, t => t.Name == "Firm-Specific Intake Add-On");
        Assert.Equal(customChecklist.Id, customChecklistAfter.Id);
        Assert.True(customChecklistAfter.IsCustom);

        var deadlinesAfter = await _fixture.Repository.GetDeadlineTemplatesAsync();
        var customDeadlineAfter = Assert.Single(deadlinesAfter, t => t.Name == "Firm-Specific Reminder");
        Assert.Equal(customDeadline.Id, customDeadlineAfter.Id);
        Assert.True(customDeadlineAfter.IsCustom);

        // Stock (is_custom=0) rows are refreshed - the same-named seed template still exists
        // exactly once (delete-then-reinsert, not an accumulating duplicate) and is still marked
        // is_custom=0.
        var stockChecklistAfter = Assert.Single(checklistAfter, t => t.Name == "Pre-Suit / Intake");
        Assert.False(stockChecklistAfter.IsCustom);
        Assert.NotEqual(stockChecklistBefore.Id, stockChecklistAfter.Id);
        Assert.Contains(stockChecklistAfter.Items, i => i.Task.Contains("ROW file review", StringComparison.OrdinalIgnoreCase));

        var stockDeadlineAfter = Assert.Single(deadlinesAfter, t => t.Name == "Service - 120 Day Deadline");
        Assert.False(stockDeadlineAfter.IsCustom);
    }

    [Fact]
    public async Task WorkTemplateCandidates_ForPipelineStageCase_IncludesRewrittenPreSuitIntakeContent()
    {
        var c = await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Fresh Intake Case",
            County = "Pulaski",
            Status = "Pipeline",
            CaseStatus = "Pipeline",
            Track = "Contested",
        });

        var candidates = await _fixture.Repository.GetWorkTemplateCandidatesAsync(c.Id);
        var intakeTasks = candidates.Where(x => x.Kind == "Task" && x.Stage == "Pipeline").Select(x => x.Title).ToList();

        Assert.Contains(intakeTasks, t => t.Contains("condemnation pre-file sheet", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(intakeTasks, t => t.Contains("Title LA sends the file to the Title Attorney", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(intakeTasks, t => t.Contains("ROW file review", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(intakeTasks, t => t.Contains("Lawsuit file review", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GenerateChecklist_ForActiveLitigationCase_ProducesRewrittenDiscoveryCoreContent()
    {
        var c = await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Fresh Discovery Case",
            County = "Pulaski",
            Status = "Active",
            CaseStatus = "Active Litigation",
            Stage = "Discovery & Evaluation",
            Track = "Contested",
            FilingDate = "2026-01-01",
        });

        var added = await _fixture.Repository.GenerateChecklistAsync(c.Id);
        Assert.True(added > 0);

        var tasks = (await _fixture.Repository.GetChecklistItemsAsync(c.Id)).Select(x => x.Task).ToList();
        Assert.Contains(tasks, t => t.Contains("rejection of the taking or a counterclaim", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tasks, t => t.Contains("Deputy Chief Counsel approval before sending", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tasks, t => t.Contains("30 days from service", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tasks, t => t.Contains("Review landowner's appraisal against checklist", StringComparison.OrdinalIgnoreCase));
    }
}
