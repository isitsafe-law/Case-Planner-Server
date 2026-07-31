using CasePlanner.Web.Server.Models;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CasePlanner.Web.Server.Tests;

public sealed class PortableUpgradeValidationTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task ReinitializingOlderSchemaPreservesCaseDataAndRestoresGenerationReadiness()
    {
        var legacyCase = await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Legacy Upgrade Fixture",
            CaseNumber = "UPGRADE-001",
            County = "Pulaski",
            Landowner = "Legacy Landowner",
            OpposingCounsel = "Legacy Counsel",
            Status = "Active",
            CaseStatus = "Active Litigation",
            Track = "Contested",
        });
        await _fixture.Repository.SaveCaseDefendantAsync(new CaseDefendantRecord { CaseId = legacyCase.Id, Name = "Legacy Party" });

        await using (var connection = new SqliteConnection($"Data Source={_fixture.DatabasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM app_settings WHERE key = 'opposing_counsel_migrated_v1';
                DELETE FROM case_opposing_attorneys WHERE case_id = @caseId;
                DROP INDEX IF EXISTS idx_cases_current_holder;
                ALTER TABLE cases DROP COLUMN case_style;
                ALTER TABLE cases DROP COLUMN current_holder;
                ALTER TABLE case_defendants DROP COLUMN party_role;
                DROP TABLE IF EXISTS document_generations;
                """;
            command.Parameters.AddWithValue("@caseId", legacyCase.Id);
            await command.ExecuteNonQueryAsync();
        }

        await _fixture.Repository.InitializeAsync();

        var restoredCase = (await _fixture.Repository.GetCasesAsync("UPGRADE-001", "", "", "", true)).Single();
        Assert.Equal(legacyCase.Id, restoredCase.Id);
        Assert.Equal("Legacy Upgrade Fixture", restoredCase.CaseName);
        Assert.Equal("Legacy Landowner", restoredCase.Landowner);
        Assert.Contains(await _fixture.Repository.GetOpposingAttorneysAsync(legacyCase.Id), x => x.Name == "Legacy Counsel");
        Assert.Equal("Defendant", Assert.Single(await _fixture.Repository.GetCaseDefendantsAsync(legacyCase.Id)).PartyRole);

        var generation = await _fixture.Repository.GenerateDocumentPlatformDocumentAsync(
            legacyCase.Id,
            "interrogatories_platform",
            [],
            new Dictionary<string, string>(),
            null);

        Assert.True(File.Exists(generation.OutputPath));
        Assert.NotEmpty(await _fixture.Repository.GetDocumentGenerationsForCaseAsync(legacyCase.Id));

        var quality = await _fixture.Repository.GetDataQualityReportAsync();
        Assert.Contains(quality.Issues, x => x.Key == "missing-template-files" && x.Count == 0);
    }
}
