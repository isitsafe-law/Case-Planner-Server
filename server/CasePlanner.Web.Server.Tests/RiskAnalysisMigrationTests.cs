using Microsoft.Data.Sqlite;

namespace CasePlanner.Web.Server.Tests;

// Exercises MigrateRiskAnalysisRowsToListV1Async against a real (throwaway, temp-file) SQLite
// database - not the live app data. RepositoryTestFixture.CreateAsync() already runs
// InitializeAsync() once (finding zero legacy rows to convert), so these tests reset the
// migration's completion flag and insert a legacy dict-shaped row directly, then re-run
// InitializeAsync() to force the migration to actually convert something.
public class RiskAnalysisMigrationTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task ResetMigrationFlagAndInsertLegacyRowAsync(long caseId, string legacyJson)
    {
        await using var connection = new SqliteConnection($"Data Source={_fixture.DatabasePath}");
        await connection.OpenAsync();

        var deleteFlag = connection.CreateCommand();
        deleteFlag.CommandText = "DELETE FROM app_settings WHERE key='risk_analysis_rows_to_list_v1_complete'";
        await deleteFlag.ExecuteNonQueryAsync();

        var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO risk_analyses (case_id, narrative, rows_json, created_at, updated_at)
            VALUES (@case_id, 'legacy test narrative', @rows_json, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')
            """;
        insert.Parameters.AddWithValue("@case_id", caseId);
        insert.Parameters.AddWithValue("@rows_json", legacyJson);
        await insert.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Migration_ConvertsLegacyDictionaryToOrderedListWithInferredOfferMakerAndSplit()
    {
        const string legacyJson = """
            {
                "LandownerOpinionOfValue": { "JustCompensation": 150000, "LandownerFeesCosts": 0, "AshcCosts": 0, "HourlyFeesRisk": 40000 },
                "LandownerAppraisal": { "JustCompensation": 140000, "LandownerFeesCosts": 5000, "AshcCosts": 0, "HourlyFeesRisk": 40000 },
                "AshcFirstOffer": { "JustCompensation": 100000, "LandownerFeesCosts": 0, "AshcCosts": 2000, "HourlyFeesRisk": 40000 },
                "AshcCounteroffer": { "JustCompensation": 110000, "LandownerFeesCosts": 0, "AshcCosts": 2000, "HourlyFeesRisk": 40000 },
                "LandownerCounteroffer": { "JustCompensation": 135000, "LandownerFeesCosts": 3000, "AshcCosts": 0, "HourlyFeesRisk": 40000 }
            }
            """;
        await ResetMigrationFlagAndInsertLegacyRowAsync(1, legacyJson);

        await _fixture.Repository.InitializeAsync();

        var result = await _fixture.Repository.GetRiskAnalysisAsync(1);

        // 5 primary rows + 3 split rows (AshcFirstOffer, AshcCounteroffer, LandownerCounteroffer).
        var primaryRows = result.Rows.Where(r => !r.IsSplit).ToList();
        Assert.Equal(5, primaryRows.Count);

        var opinionOfValue = Assert.Single(primaryRows, r => r.RowKey == "LandownerOpinionOfValue");
        Assert.Equal("Landowner's Opinion of Value", opinionOfValue.Label);
        Assert.Equal("Landowner", opinionOfValue.OfferMaker);
        Assert.Equal(150000, opinionOfValue.JustCompensation);

        var firstOffer = Assert.Single(primaryRows, r => r.RowKey == "AshcFirstOffer");
        Assert.Equal("ASHC", firstOffer.OfferMaker);
        Assert.Contains(result.Rows, r => r.RowKey == "AshcFirstOfferSplit" && r.IsSplit);

        var landownerCounter = Assert.Single(primaryRows, r => r.RowKey == "LandownerCounteroffer");
        Assert.Equal("Landowner", landownerCounter.OfferMaker);
        Assert.Contains(result.Rows, r => r.RowKey == "LandownerCounterofferSplit" && r.IsSplit);

        // Rows without a historical split (opinion of value, appraisal) should NOT have gained one.
        Assert.DoesNotContain(result.Rows, r => r.RowKey == "LandownerOpinionOfValueSplit");
        Assert.DoesNotContain(result.Rows, r => r.RowKey == "LandownerAppraisalSplit");
    }

    [Fact]
    public async Task Migration_IsIdempotent_DoesNotReconvertOnSecondRun()
    {
        const string legacyJson = """{"AshcFirstOffer": {"JustCompensation": 100000, "LandownerFeesCosts": 0, "AshcCosts": 0, "HourlyFeesRisk": 40000}}""";
        await ResetMigrationFlagAndInsertLegacyRowAsync(1, legacyJson);
        await _fixture.Repository.InitializeAsync();

        // Running InitializeAsync again should be a no-op for this migration (flag already set) -
        // if it weren't gated, re-running against already-list-shaped JSON would throw trying to
        // deserialize a JSON array as a Dictionary.
        await _fixture.Repository.InitializeAsync();

        var result = await _fixture.Repository.GetRiskAnalysisAsync(1);
        Assert.Single(result.Rows, r => r.RowKey == "AshcFirstOffer");
    }

    [Fact]
    public async Task NarrativeAshcOfferLookup_ResolvesCorrectly_AfterMigration()
    {
        const string legacyJson = """
            {
                "AshcFirstOffer": { "JustCompensation": 100000, "LandownerFeesCosts": 0, "AshcCosts": 0, "HourlyFeesRisk": 40000 },
                "AshcCounteroffer": { "JustCompensation": 110000, "LandownerFeesCosts": 0, "AshcCosts": 0, "HourlyFeesRisk": 40000 },
                "LandownerCounteroffer": { "JustCompensation": 135000, "LandownerFeesCosts": 0, "AshcCosts": 0, "HourlyFeesRisk": 40000 }
            }
            """;
        await ResetMigrationFlagAndInsertLegacyRowAsync(1, legacyJson);
        await _fixture.Repository.InitializeAsync();

        var result = await _fixture.Repository.GetRiskAnalysisAsync(1);
        // Mirrors BuildRiskNarrativeText's lookup: first ASHC-maker non-split row, last
        // Landowner-maker non-split row - should land on AshcFirstOffer and LandownerCounteroffer,
        // exactly matching the old hardcoded-RowKey lookup's behavior for a migrated case.
        var ashcOffer = result.Rows.FirstOrDefault(r => !r.IsSplit && r.OfferMaker == "ASHC");
        var counteroffer = result.Rows.LastOrDefault(r => !r.IsSplit && r.OfferMaker == "Landowner");
        Assert.Equal("AshcFirstOffer", ashcOffer?.RowKey);
        Assert.Equal("LandownerCounteroffer", counteroffer?.RowKey);
    }
}
