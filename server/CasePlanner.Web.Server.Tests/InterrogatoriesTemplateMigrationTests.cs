using Microsoft.Data.Sqlite;

namespace CasePlanner.Web.Server.Tests;

// Regression coverage for EnsureInterrogatoriesAllIssueTagSectionsAsync
// (CasePlannerRepository.DocumentPlatform.cs), the one-time migration that upgrades the
// interrogatories_platform template from its seed (v1, Drainage-only) shape to the full
// all-issue-tag-section (v2) shape. RepositoryTestFixture.CreateAsync() already runs
// InitializeAsync() once, so by the time these tests start the migration has already run for
// real - these tests exercise its idempotence and, for the recovery case, hand-corrupt the
// database into the historical broken fingerprint (every version deactivated, newest version
// with zero sections) and confirm a subsequent InitializeAsync() call repairs it rather than
// silently giving up forever, which is what the original (non-atomic, no-recovery) version of
// the method did.
public sealed class InterrogatoriesTemplateMigrationTests : IAsyncLifetime
{
    private const string TemplateKey = "interrogatories_platform";

    // Mirrors CasePlannerRepository.DocumentPlatform.cs' InterrogatoriesIssueTagSections exactly -
    // that field is private, so this is a deliberate, small duplication rather than a shared
    // production-code dependency.
    private static readonly string[] ExpectedSectionKeys =
    [
        "FullTaking", "EasementOnly", "TemporaryConstructionEasement", "SeveranceDamages",
        "AccessChangeOfAccess", "Drainage", "LandlockedRemainder", "Minerals", "Timber",
        "BillboardSign", "LeaseholdTenantInterest", "LienholderMortgage", "EstateProbate",
        "UnknownHeirsOwners", "UtilityConflict",
    ];

    private RepositoryTestFixture _fixture = null!;
    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    // Directly corrupts the on-disk database into the historical broken fingerprint: every
    // version row for the template deactivated, and the newest version's section rows wiped out -
    // exactly what a crash partway through the old non-transactional section-insert loop could
    // leave behind. Returns the newest version's id so tests can assert against it.
    private async Task<long> BreakActiveVersionAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={_fixture.DatabasePath}");
        await connection.OpenAsync();

        var templateIdCmd = connection.CreateCommand();
        templateIdCmd.CommandText = "SELECT id FROM document_templates WHERE template_key = @key";
        templateIdCmd.Parameters.AddWithValue("@key", TemplateKey);
        var templateId = Convert.ToInt64(await templateIdCmd.ExecuteScalarAsync());

        var newestVersionIdCmd = connection.CreateCommand();
        newestVersionIdCmd.CommandText = "SELECT id FROM document_template_versions WHERE template_id = @id ORDER BY version DESC LIMIT 1";
        newestVersionIdCmd.Parameters.AddWithValue("@id", templateId);
        var newestVersionId = Convert.ToInt64(await newestVersionIdCmd.ExecuteScalarAsync());

        var deactivate = connection.CreateCommand();
        deactivate.CommandText = "UPDATE document_template_versions SET is_active = 0 WHERE template_id = @id";
        deactivate.Parameters.AddWithValue("@id", templateId);
        await deactivate.ExecuteNonQueryAsync();

        var wipeSections = connection.CreateCommand();
        wipeSections.CommandText = "DELETE FROM document_template_sections WHERE template_version_id = @v";
        wipeSections.Parameters.AddWithValue("@v", newestVersionId);
        await wipeSections.ExecuteNonQueryAsync();

        return newestVersionId;
    }

    [Fact]
    public async Task AfterInitialization_TemplateHasExactlyOneActiveVersionWithAllIssueTagSections()
    {
        var all = await _fixture.Repository.GetAllDocumentTemplatesForAdminAsync();
        var summary = Assert.Single(all, t => t.Template.TemplateKey == TemplateKey);

        Assert.NotNull(summary.ActiveVersion);
        Assert.Single(summary.Versions, v => v.IsActive);

        var sectionKeys = summary.Sections.Select(s => s.SectionKey).ToList();
        Assert.Equal(ExpectedSectionKeys.Length, sectionKeys.Count);
        Assert.Equal(sectionKeys.Count, sectionKeys.Distinct().Count()); // no duplicates
        foreach (var key in ExpectedSectionKeys)
        {
            Assert.Contains(key, sectionKeys);
        }
    }

    [Fact]
    public async Task RunningInitializationTwice_IsIdempotent()
    {
        var before = Assert.Single(
            (await _fixture.Repository.GetAllDocumentTemplatesForAdminAsync()),
            t => t.Template.TemplateKey == TemplateKey);
        var activeVersionIdBefore = before.ActiveVersion!.Id;
        var sectionCountBefore = before.Sections.Count;

        await _fixture.Repository.InitializeAsync();

        var after = Assert.Single(
            (await _fixture.Repository.GetAllDocumentTemplatesForAdminAsync()),
            t => t.Template.TemplateKey == TemplateKey);

        Assert.Equal(activeVersionIdBefore, after.ActiveVersion!.Id);
        Assert.Equal(sectionCountBefore, after.Sections.Count);
        Assert.Single(after.Versions, v => v.IsActive);
        Assert.Equal(before.Versions.Count, after.Versions.Count); // no extra version minted
    }

    [Fact]
    public async Task RecoversFromBrokenState_NoActiveVersionAndZeroSectionsOnNewestVersion()
    {
        var beforeBreak = Assert.Single(
            (await _fixture.Repository.GetAllDocumentTemplatesForAdminAsync()),
            t => t.Template.TemplateKey == TemplateKey);
        var versionCountBeforeBreak = beforeBreak.Versions.Count;

        var brokenVersionId = await BreakActiveVersionAsync();

        // Confirm the corruption actually landed in the broken fingerprint the bug report
        // describes before relying on it: no active version anywhere for the template.
        var midway = await _fixture.Repository.GetAllDocumentTemplatesForAdminAsync();
        var midwaySummary = Assert.Single(midway, t => t.Template.TemplateKey == TemplateKey);
        Assert.Null(midwaySummary.ActiveVersion);
        Assert.DoesNotContain(midwaySummary.Versions, v => v.IsActive);

        // Re-running InitializeAsync (what happens on every app startup) must repair this rather
        // than the old `if (active is null) return;` guard's silent-forever-broken behavior.
        await _fixture.Repository.InitializeAsync();

        var repaired = await _fixture.Repository.GetAllDocumentTemplatesForAdminAsync();
        var repairedSummary = Assert.Single(repaired, t => t.Template.TemplateKey == TemplateKey);

        Assert.NotNull(repairedSummary.ActiveVersion);
        Assert.Single(repairedSummary.Versions, v => v.IsActive);
        Assert.Equal(brokenVersionId, repairedSummary.ActiveVersion!.Id); // reactivated in place, no new version/docx minted

        var sectionKeys = repairedSummary.Sections.Select(s => s.SectionKey).ToList();
        Assert.Equal(ExpectedSectionKeys.Length, sectionKeys.Count);
        Assert.Equal(sectionKeys.Count, sectionKeys.Distinct().Count());
        foreach (var key in ExpectedSectionKeys)
        {
            Assert.Contains(key, sectionKeys);
        }

        // Repair should not have minted an additional version row on top of the broken one.
        Assert.Equal(versionCountBeforeBreak, repairedSummary.Versions.Count);
    }
}
