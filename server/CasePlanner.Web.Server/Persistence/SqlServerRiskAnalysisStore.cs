using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using CasePlanner.Data;
using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;
using Microsoft.Data.SqlClient;

namespace CasePlanner.Web.Server.Persistence;

public sealed class SqlServerRiskAnalysisStore(
    IDatabaseConnectionFactory connections,
    IHttpContextAccessor accessor,
    SqlServerCaseCatalogReader cases) : SqlServerLitigationStoreBase(connections, accessor)
{
    public string Provider => "SqlServer";

    public async Task<RiskAnalysisResult> PreviewAsync(long caseId, RiskAnalysisInput input, CancellationToken token = default)
    {
        var caseRecord = await GetCaseAsync(caseId, token);
        input.CaseId = caseId;
        return RiskAnalysisEngine.Compute(caseRecord, input);
    }

    public async Task<RiskAnalysisResult> GetAsync(long caseId, CancellationToken token = default)
    {
        var caseRecord = await GetCaseAsync(caseId, token);
        await using var connection = Connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,narrative,rows_json,analysis_date,interest_rate,contingency_fee_percent,
                   created_at,updated_at,row_version
            FROM dbo.risk_analyses
            WHERE case_id=@caseId AND is_deleted=0
            """;
        command.Parameters.Add(new SqlParameter("@caseId", caseId));
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token))
            return RiskAnalysisEngine.Compute(caseRecord, new RiskAnalysisInput { CaseId = caseId });

        var input = ReadInput(reader, caseId, 1);
        var result = RiskAnalysisEngine.Compute(caseRecord, input);
        result.Id = reader.GetInt64(0);
        result.CreatedAt = Text(reader, 6) ?? "";
        result.UpdatedAt = Text(reader, 7) ?? "";
        result.RowVersion = Convert.ToBase64String((byte[])reader.GetValue(8));
        return result;
    }

    public async Task<List<RiskAnalysisHistoryRecord>> GetHistoryAsync(long caseId, CancellationToken token = default)
    {
        var result = new List<RiskAnalysisHistoryRecord>();
        await using var connection = Connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,case_id,analysis_date,formula_version,narrative,rows_json,interest_rate,
                   contingency_fee_percent,key_scenario_label,key_scenario_value,key_scenario_order,
                   created_at,row_version
            FROM dbo.risk_analysis_history
            WHERE case_id=@caseId AND is_deleted=0
            ORDER BY created_at DESC,id DESC
            """;
        command.Parameters.Add(new SqlParameter("@caseId", caseId));
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            result.Add(new RiskAnalysisHistoryRecord
            {
                Id = reader.GetInt64(0),
                CaseId = reader.GetInt64(1),
                AnalysisDate = Text(reader, 2) ?? "",
                FormulaVersion = Text(reader, 3) ?? "risk-v1",
                Narrative = Text(reader, 4),
                Rows = DeserializeRows(Text(reader, 5)),
                InterestRate = Decimal(reader, 6, 0.06m),
                ContingencyFeePercent = Decimal(reader, 7, 0.30m),
                KeyScenarioLabel = Text(reader, 8),
                KeyScenarioValue = reader.IsDBNull(9) ? null : Convert.ToDecimal(reader.GetValue(9), CultureInfo.InvariantCulture),
                KeyScenarioOrder = reader.IsDBNull(10) ? null : Convert.ToInt32(reader.GetValue(10), CultureInfo.InvariantCulture),
                CreatedAt = Text(reader, 11) ?? "",
                RowVersion = Convert.ToBase64String((byte[])reader.GetValue(12))
            });
        }
        return result;
    }

    public async Task<RiskAnalysisResult> GetHistorySnapshotAsync(long caseId, long historyId, CancellationToken token = default)
    {
        var caseRecord = await GetCaseAsync(caseId, token);
        await using var connection = Connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,narrative,rows_json,analysis_date,interest_rate,contingency_fee_percent,
                   created_at,row_version
            FROM dbo.risk_analysis_history
            WHERE id=@id AND case_id=@caseId AND is_deleted=0
            """;
        command.Parameters.Add(new SqlParameter("@id", historyId));
        command.Parameters.Add(new SqlParameter("@caseId", caseId));
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new InvalidOperationException("Risk analysis snapshot not found.");
        var input = ReadInput(reader, caseId, 1);
        var result = RiskAnalysisEngine.Compute(caseRecord, input);
        result.Id = reader.GetInt64(0);
        result.CreatedAt = Text(reader, 6) ?? "";
        result.UpdatedAt = result.CreatedAt;
        result.RowVersion = Convert.ToBase64String((byte[])reader.GetValue(7));
        return result;
    }

    public async Task<RiskAnalysisResult> SaveAsync(RiskAnalysisInput input, CancellationToken token = default)
    {
        var caseRecord = await GetCaseAsync(input.CaseId, token);
        var computed = RiskAnalysisEngine.Compute(caseRecord, input);
        var rowsJson = JsonSerializer.Serialize(input.Rows);
        var now = DateTime.UtcNow.ToString("O");
        var analysisDate = computed.AnalysisDate;
        var keyScenario = input.Rows
            .Select((row, index) => (Row: row, Index: index))
            .LastOrDefault(item => item.Row.JustCompensation is > 0);

        await using var connection = Connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await EnsureCaseExistsAsync(connection, transaction, input.CaseId, token);

        long existingId = 0;
        bool existingDeleted = false;
        await using (var lookup = connection.CreateCommand())
        {
            lookup.Transaction = transaction;
            lookup.CommandText = "SELECT id,is_deleted FROM dbo.risk_analyses WHERE case_id=@caseId";
            lookup.Parameters.Add(new SqlParameter("@caseId", input.CaseId));
            await using var reader = await lookup.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token))
            {
                existingId = reader.GetInt64(0);
                existingDeleted = Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture) == 1;
            }
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (existingId == 0)
        {
            command.CommandText = """
                INSERT INTO dbo.risk_analyses
                    (case_id,narrative,rows_json,analysis_date,interest_rate,contingency_fee_percent,
                     created_at,updated_at,created_by_user_id,updated_by_user_id)
                OUTPUT INSERTED.id,INSERTED.created_at,INSERTED.updated_at,INSERTED.row_version
                VALUES
                    (@caseId,@narrative,@rows,@analysisDate,@interest,@contingency,
                     @now,@now,@actor,@actor)
                """;
        }
        else if (existingDeleted)
        {
            command.CommandText = """
                UPDATE dbo.risk_analyses
                SET narrative=@narrative,rows_json=@rows,analysis_date=@analysisDate,
                    interest_rate=@interest,contingency_fee_percent=@contingency,
                    created_at=@now,updated_at=@now,created_by_user_id=@actor,updated_by_user_id=@actor,
                    is_deleted=0,deleted_utc=NULL,deleted_by_user_id=NULL
                OUTPUT INSERTED.id,INSERTED.created_at,INSERTED.updated_at,INSERTED.row_version
                WHERE id=@id AND is_deleted=1
                """;
            command.Parameters.Add(new SqlParameter("@id", existingId));
        }
        else
        {
            command.CommandText = """
                UPDATE dbo.risk_analyses
                SET narrative=@narrative,rows_json=@rows,analysis_date=@analysisDate,
                    interest_rate=@interest,contingency_fee_percent=@contingency,
                    updated_at=@now,updated_by_user_id=@actor
                OUTPUT INSERTED.id,INSERTED.created_at,INSERTED.updated_at,INSERTED.row_version
                WHERE id=@id AND row_version=@version AND is_deleted=0
                """;
            command.Parameters.Add(new SqlParameter("@id", existingId));
            command.Parameters.Add(new SqlParameter("@version", ExpectedVersion(input.RowVersion, "risk analysis", existingId)));
        }
        AddAnalysisParameters(command, input, rowsJson, analysisDate, now);
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) throw new WorkItemConcurrencyException("Risk analysis", existingId);
            computed.Id = reader.GetInt64(0);
            computed.CreatedAt = Text(reader, 1) ?? now;
            computed.UpdatedAt = Text(reader, 2) ?? now;
            computed.RowVersion = Convert.ToBase64String((byte[])reader.GetValue(3));
        }

        long historyId;
        await using (var history = connection.CreateCommand())
        {
            history.Transaction = transaction;
            history.CommandText = """
                INSERT INTO dbo.risk_analysis_history
                    (case_id,analysis_date,formula_version,narrative,rows_json,interest_rate,
                     contingency_fee_percent,key_scenario_label,key_scenario_value,key_scenario_order,
                     created_at,created_by_user_id)
                OUTPUT INSERTED.id
                VALUES
                    (@caseId,@analysisDate,'risk-v1',@narrative,@rows,@interest,
                     @contingency,@keyLabel,@keyValue,@keyOrder,@now,@actor)
                """;
            AddAnalysisParameters(history, input, rowsJson, analysisDate, now);
            history.Parameters.Add(new SqlParameter("@keyLabel", (object?)keyScenario.Row?.Label ?? DBNull.Value));
            history.Parameters.Add(new SqlParameter("@keyValue", (object?)keyScenario.Row?.JustCompensation ?? DBNull.Value));
            history.Parameters.Add(new SqlParameter("@keyOrder", keyScenario.Row is null ? DBNull.Value : keyScenario.Index));
            historyId = Convert.ToInt64(await history.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
        }
        await AuditAsync(connection, transaction, input.CaseId, existingId == 0 || existingDeleted ? "RiskAnalysisCreated" : "RiskAnalysisUpdated", "RiskAnalysis", computed.Id, token);
        await AuditAsync(connection, transaction, input.CaseId, "RiskAnalysisSnapshotCreated", "RiskAnalysisHistory", historyId, token);
        await transaction.CommitAsync(token);
        return computed;
    }

    public Task DeleteAsync(long id, string? rowVersion, CancellationToken token = default) =>
        SoftDeleteAsync("risk_analyses", "Risk analysis", "RiskAnalysis", id, rowVersion, token);

    public Task DeleteHistoryAsync(long id, string? rowVersion, CancellationToken token = default) =>
        SoftDeleteAsync("risk_analysis_history", "Risk analysis snapshot", "RiskAnalysisHistory", id, rowVersion, token);

    private async Task<CaseRecord> GetCaseAsync(long caseId, CancellationToken token)
    {
        var all = await cases.GetCasesAsync(new CaseCatalogQuery(IncludeClosed: true), token);
        return all.FirstOrDefault(item => item.Id == caseId)
            ?? throw new InvalidOperationException($"Case {caseId} does not exist in SQL Server.");
    }

    private void AddAnalysisParameters(DbCommand command, RiskAnalysisInput input, string rowsJson, string analysisDate, string now)
    {
        command.Parameters.Add(new SqlParameter("@caseId", input.CaseId));
        command.Parameters.Add(new SqlParameter("@narrative", Db(input.Narrative)));
        command.Parameters.Add(new SqlParameter("@rows", rowsJson));
        command.Parameters.Add(new SqlParameter("@analysisDate", analysisDate));
        command.Parameters.Add(new SqlParameter("@interest", input.InterestRate <= 0 ? 0.06m : input.InterestRate));
        command.Parameters.Add(new SqlParameter("@contingency", input.ContingencyFeePercent < 0 ? 0.30m : input.ContingencyFeePercent));
        command.Parameters.Add(new SqlParameter("@now", now));
        command.Parameters.Add(new SqlParameter("@actor", (object?)ActorUserId ?? DBNull.Value));
    }

    private static RiskAnalysisInput ReadInput(DbDataReader reader, long caseId, int narrativeIndex) => new()
    {
        CaseId = caseId,
        Narrative = Text(reader, narrativeIndex),
        Rows = DeserializeRows(Text(reader, narrativeIndex + 1)),
        AnalysisDate = Text(reader, narrativeIndex + 2),
        InterestRate = Decimal(reader, narrativeIndex + 3, 0.06m),
        ContingencyFeePercent = Decimal(reader, narrativeIndex + 4, 0.30m)
    };

    private static List<RiskAnalysisRowInput> DeserializeRows(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<RiskAnalysisRowInput>>(json) ?? [];

    private static decimal Decimal(DbDataReader reader, int index, decimal fallback) =>
        reader.IsDBNull(index) ? fallback : Convert.ToDecimal(reader.GetValue(index), CultureInfo.InvariantCulture);
}

public sealed class SqlServerRiskOfferStore(
    IDatabaseConnectionFactory connections,
    IHttpContextAccessor accessor) : SqlServerLitigationStoreBase(connections, accessor)
{
    public async Task<List<RiskAnalysisOfferLogEntry>> GetAsync(long? caseId, CancellationToken token = default)
    {
        var result = new List<RiskAnalysisOfferLogEntry>();
        await using var connection = Connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,case_id,offer_date,party,amount,updated_at,row_version
            FROM dbo.risk_analysis_offer_log
            WHERE is_deleted=0 AND (@caseId IS NULL OR case_id=@caseId)
            ORDER BY CASE WHEN offer_date IS NULL THEN 1 ELSE 0 END,offer_date,id
            """;
        command.Parameters.Add(new SqlParameter("@caseId", (object?)caseId ?? DBNull.Value));
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            result.Add(new RiskAnalysisOfferLogEntry
            {
                Id = reader.GetInt64(0),
                CaseId = reader.GetInt64(1),
                OfferDate = Date(Text(reader, 2)),
                Party = Text(reader, 3) ?? "",
                Amount = reader.IsDBNull(4) ? null : Convert.ToDecimal(reader.GetValue(4), CultureInfo.InvariantCulture),
                UpdatedAt = Text(reader, 5),
                RowVersion = Convert.ToBase64String((byte[])reader.GetValue(6))
            });
        }
        return result;
    }

    public async Task<RiskAnalysisOfferLogEntry> SaveAsync(RiskAnalysisOfferLogEntry model, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(model.Party)) throw new ArgumentException("Offer party is required.");
        var isNew = model.Id == 0;
        await using var connection = Connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        model.CaseId = await ResolveCaseIdAsync(connection, transaction, "risk_analysis_offer_log", model.Id, model.CaseId, token);
        var now = DateTime.UtcNow.ToString("O");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (isNew)
        {
            command.CommandText = """
                INSERT INTO dbo.risk_analysis_offer_log
                    (case_id,offer_date,party,amount,created_at,updated_at,created_by_user_id,updated_by_user_id)
                OUTPUT INSERTED.id,INSERTED.row_version
                VALUES (@caseId,@date,@party,@amount,@now,@now,@actor,@actor)
                """;
        }
        else
        {
            command.CommandText = """
                UPDATE dbo.risk_analysis_offer_log
                SET offer_date=@date,party=@party,amount=@amount,updated_at=@now,updated_by_user_id=@actor
                OUTPUT INSERTED.id,INSERTED.row_version
                WHERE id=@id AND row_version=@version AND is_deleted=0
                """;
            command.Parameters.Add(new SqlParameter("@id", model.Id));
            command.Parameters.Add(new SqlParameter("@version", ExpectedVersion(model.RowVersion, "risk analysis offer", model.Id)));
        }
        command.Parameters.Add(new SqlParameter("@caseId", model.CaseId));
        command.Parameters.Add(new SqlParameter("@date", Db(Date(model.OfferDate))));
        command.Parameters.Add(new SqlParameter("@party", model.Party.Trim()));
        command.Parameters.Add(new SqlParameter("@amount", (object?)model.Amount ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@now", now));
        command.Parameters.Add(new SqlParameter("@actor", (object?)ActorUserId ?? DBNull.Value));
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) throw new WorkItemConcurrencyException("Risk analysis offer", model.Id);
            model.Id = reader.GetInt64(0);
            model.RowVersion = Convert.ToBase64String((byte[])reader.GetValue(1));
        }
        model.OfferDate = Date(model.OfferDate);
        model.UpdatedAt = now;
        await AuditAsync(connection, transaction, model.CaseId, isNew ? "RiskAnalysisOfferCreated" : "RiskAnalysisOfferUpdated", "RiskAnalysisOffer", model.Id, token);
        await transaction.CommitAsync(token);
        return model;
    }

    public Task DeleteAsync(long id, string? rowVersion, CancellationToken token = default) =>
        SoftDeleteAsync("risk_analysis_offer_log", "Risk analysis offer", "RiskAnalysisOffer", id, rowVersion, token);
}
