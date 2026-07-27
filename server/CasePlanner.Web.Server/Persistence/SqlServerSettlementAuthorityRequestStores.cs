using System.Data.Common;
using CasePlanner.Data;
using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Security;
using Microsoft.Data.SqlClient;

namespace CasePlanner.Web.Server.Persistence;

// SQL Server side of the Settlement Authority workflow (see SettlementAuthorityRequestStores.cs
// for the shared rationale). dbo.settlement_authority_requests (058_settlement_authority_requests.sql)
// updates in place and carries a row_version rowversion column, unlike the append-only
// pipeline_holder_approvals table - so this needs the same EnsureCaseExistsAsync/AuditAsync
// helpers SqlServerPipelineHolderApprovalStore uses (via SqlServerLitigationStoreBase), plus a
// SqlServerActivityStore dependency to write the user-facing activity_log entry after each action
// - the exact composition SqlServerCaseQuickActionService already uses for its own quick actions
// (see CaseQuickActionService.cs). There is no live SQL Server sandbox available here to exercise
// this against a real pilot instance - same caveat already noted for the rest of the dormant
// multi-user foundation.
public sealed class SqlServerSettlementAuthorityRequestStore(
    IDatabaseConnectionFactory connections,
    IHttpContextAccessor accessor,
    IApplicationActorContext actor,
    SqlServerActivityStore activity)
    : SqlServerLitigationStoreBase(connections, accessor), ISettlementAuthorityRequestStore
{
    public string Provider => "SqlServer";

    private const string Columns = """
        id, case_id, requested_amount, requesting_attorney, request_notes, status,
        granted_amount, requested_at, requested_by_user_id, requested_by_display,
        decided_at, decided_by_user_id, decided_by_display, decided_by_role, decision_comment, row_version
        """;

    public async Task<List<SettlementAuthorityRequestRecord>> GetAsync(long? caseId, CancellationToken token = default)
    {
        var result = new List<SettlementAuthorityRequestRecord>();
        await using var connection = Connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM dbo.settlement_authority_requests WHERE (@caseId IS NULL OR case_id=@caseId) ORDER BY id DESC";
        command.Parameters.Add(new SqlParameter("@caseId", (object?)caseId ?? DBNull.Value));
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(Read(reader));
        return result;
    }

    public async Task<SettlementAuthorityRequestRecord> CreateAsync(long caseId, CreateSettlementAuthorityRequest request, CancellationToken token = default)
    {
        long id;
        await using (var connection = Connections.CreateConnection())
        {
            await connection.OpenAsync(token);
            await using var transaction = await connection.BeginTransactionAsync(token);
            await EnsureCaseExistsAsync(connection, transaction, caseId, token);

            await using (var openCommand = connection.CreateCommand())
            {
                openCommand.Transaction = transaction;
                openCommand.CommandText = "SELECT COUNT(*) FROM dbo.settlement_authority_requests WHERE case_id=@caseId AND status IN ('Pending','InfoRequested')";
                openCommand.Parameters.Add(new SqlParameter("@caseId", caseId));
                var openCount = Convert.ToInt64(await openCommand.ExecuteScalarAsync(token));
                if (openCount > 0)
                    throw new InvalidOperationException($"Case {caseId} already has an open Settlement Authority request. Decide it before submitting another.");
            }

            var now = DateTime.UtcNow.ToString("O");
            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO dbo.settlement_authority_requests
                        (case_id, requested_amount, requesting_attorney, request_notes, status, requested_at, requested_by_user_id, requested_by_display)
                    OUTPUT INSERTED.id
                    VALUES (@caseId, @amount, @attorney, @notes, 'Pending', @requestedAt, @actorId, @actorDisplay)
                    """;
                insert.Parameters.Add(new SqlParameter("@caseId", caseId));
                insert.Parameters.Add(new SqlParameter("@amount", request.RequestedAmount));
                insert.Parameters.Add(new SqlParameter("@attorney", Db(request.RequestingAttorney)));
                insert.Parameters.Add(new SqlParameter("@notes", Db(request.RequestNotes)));
                insert.Parameters.Add(new SqlParameter("@requestedAt", now));
                insert.Parameters.Add(new SqlParameter("@actorId", (object?)actor.UserId ?? DBNull.Value));
                insert.Parameters.Add(new SqlParameter("@actorDisplay", actor.AuditLabel));
                id = Convert.ToInt64(await insert.ExecuteScalarAsync(token));
            }

            await AuditAsync(connection, transaction, caseId, "SettlementAuthorityRequestCreated", "SettlementAuthorityRequest", id, token);
            await transaction.CommitAsync(token);
        }

        // Same convention as SqlServerCaseQuickActionService's quick actions - the user-facing
        // activity_log write happens as its own call, on its own connection/transaction, after the
        // main insert has already committed.
        await activity.RecordAsync(caseId, "SettlementAuthorityRequested", request.RequestNotes, null, token);

        var created = await GetAsync(caseId, token);
        return created.First(r => r.Id == id);
    }

    public async Task<SettlementAuthorityRequestRecord> DecideAsync(long requestId, DecideSettlementAuthorityRequest decision, CancellationToken token = default)
    {
        if (decision.Action is not ("Approved" or "Denied" or "InfoRequested"))
            throw new ArgumentException("Action must be \"Approved\", \"Denied\", or \"InfoRequested\".");
        if (string.IsNullOrWhiteSpace(decision.Comment))
            throw new ArgumentException("A comment is required to decide a Settlement Authority request.");

        long caseId;
        decimal? previousCeiling;
        decimal? grantedAmount = null;

        await using (var connection = Connections.CreateConnection())
        {
            await connection.OpenAsync(token);
            await using var transaction = await connection.BeginTransactionAsync(token);

            string currentStatus;
            decimal requestedAmount;
            await using (var lookup = connection.CreateCommand())
            {
                lookup.Transaction = transaction;
                lookup.CommandText = "SELECT case_id, status, requested_amount FROM dbo.settlement_authority_requests WHERE id=@id";
                lookup.Parameters.Add(new SqlParameter("@id", requestId));
                await using var reader = await lookup.ExecuteReaderAsync(token);
                if (!await reader.ReadAsync(token))
                    throw new InvalidOperationException($"Settlement Authority request {requestId} was not found.");
                caseId = reader.GetInt64(0);
                currentStatus = Text(reader, 1) ?? "Pending";
                requestedAmount = reader.IsDBNull(2) ? 0m : reader.GetDecimal(2);
            }

            if (currentStatus is not ("Pending" or "InfoRequested"))
                throw new InvalidOperationException($"Settlement Authority request {requestId} has already been decided (status \"{currentStatus}\") and cannot be decided again.");

            await using (var caseCommand = connection.CreateCommand())
            {
                caseCommand.Transaction = transaction;
                caseCommand.CommandText = "SELECT settlement_authorized_ceiling FROM dbo.cases WHERE id=@caseId";
                caseCommand.Parameters.Add(new SqlParameter("@caseId", caseId));
                var priorValue = await caseCommand.ExecuteScalarAsync(token);
                previousCeiling = priorValue is null or DBNull ? null : Convert.ToDecimal(priorValue);
            }

            if (decision.Action == "Approved") grantedAmount = decision.GrantedAmount ?? requestedAmount;
            var now = DateTime.UtcNow.ToString("O");

            await using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE dbo.settlement_authority_requests SET
                        status=@status, granted_amount=@granted, decided_at=@decidedAt,
                        decided_by_user_id=@decidedBy, decided_by_display=@decidedByDisplay,
                        decided_by_role=@decidedByRole, decision_comment=@comment
                    WHERE id=@id
                    """;
                update.Parameters.Add(new SqlParameter("@status", decision.Action));
                update.Parameters.Add(new SqlParameter("@granted", (object?)grantedAmount ?? DBNull.Value));
                update.Parameters.Add(new SqlParameter("@decidedAt", now));
                update.Parameters.Add(new SqlParameter("@decidedBy", (object?)actor.UserId ?? DBNull.Value));
                update.Parameters.Add(new SqlParameter("@decidedByDisplay", actor.AuditLabel));
                update.Parameters.Add(new SqlParameter("@decidedByRole", Db(actor.Role)));
                update.Parameters.Add(new SqlParameter("@comment", decision.Comment));
                update.Parameters.Add(new SqlParameter("@id", requestId));
                await update.ExecuteNonQueryAsync(token);
            }

            if (decision.Action == "Approved")
            {
                await using var ceiling = connection.CreateCommand();
                ceiling.Transaction = transaction;
                ceiling.CommandText = "UPDATE dbo.cases SET settlement_authorized_ceiling=@ceiling, updated_at=@updatedAt WHERE id=@caseId";
                ceiling.Parameters.Add(new SqlParameter("@ceiling", grantedAmount!.Value));
                ceiling.Parameters.Add(new SqlParameter("@updatedAt", now));
                ceiling.Parameters.Add(new SqlParameter("@caseId", caseId));
                await ceiling.ExecuteNonQueryAsync(token);
            }

            await AuditAsync(connection, transaction, caseId, "SettlementAuthorityRequestDecided", "SettlementAuthorityRequest", requestId, token);
            await transaction.CommitAsync(token);
        }

        var previousLabel = previousCeiling.HasValue ? previousCeiling.Value.ToString("F2") : "none";
        var (activityType, newValueLabel) = decision.Action switch
        {
            "Approved" => ("SettlementAuthorityReceived", grantedAmount!.Value.ToString("F2")),
            "Denied" => ("SettlementAuthorityDenied", "Denied"),
            _ => ("SettlementAuthorityInfoRequested", "InfoRequested"),
        };
        await activity.RecordAsync(caseId, activityType, decision.Comment, null, token, "SettlementAuthorizedCeiling", previousLabel, newValueLabel);

        var updated = await GetAsync(caseId, token);
        return updated.First(r => r.Id == requestId);
    }

    private static SettlementAuthorityRequestRecord Read(DbDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        CaseId = reader.GetInt64(1),
        RequestedAmount = reader.IsDBNull(2) ? 0m : reader.GetDecimal(2),
        RequestingAttorney = Text(reader, 3),
        RequestNotes = Text(reader, 4),
        Status = Text(reader, 5) ?? "Pending",
        GrantedAmount = reader.IsDBNull(6) ? (decimal?)null : reader.GetDecimal(6),
        RequestedAt = Text(reader, 7) ?? "",
        RequestedByUserId = Text(reader, 8),
        RequestedByDisplay = Text(reader, 9),
        DecidedAt = Text(reader, 10),
        DecidedByUserId = Text(reader, 11),
        DecidedByDisplay = Text(reader, 12),
        DecidedByRole = Text(reader, 13),
        DecisionComment = Text(reader, 14),
        RowVersion = Convert.ToBase64String((byte[])reader.GetValue(15)),
    };
}
