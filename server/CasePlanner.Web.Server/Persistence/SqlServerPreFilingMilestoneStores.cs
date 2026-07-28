using CasePlanner.Data;
using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Security;
using Microsoft.Data.SqlClient;

namespace CasePlanner.Web.Server.Persistence;

// SQL Server side of the pre-filing milestone tracker (see PreFilingMilestoneStores.cs for the
// shared rationale). dbo.case_prefiling_milestones (060_case_prefiling_milestones.sql) updates in
// place per (case_id, milestone) and carries a row_version rowversion column, unlike the
// append-only pipeline_holder_approvals table - so this needs the same
// EnsureCaseExistsAsync/AuditAsync helpers SqlServerSettlementAuthorityRequestStore uses (via
// SqlServerLitigationStoreBase), plus a SqlServerActivityStore dependency to write the user-facing
// activity_log entry after each mark/unmark - the exact composition
// SqlServerSettlementAuthorityRequestStore already uses for its own actions. There is no live SQL
// Server sandbox available here to exercise this against a real pilot instance - same caveat
// already noted for the rest of the dormant multi-user foundation.
public sealed class SqlServerPreFilingMilestoneStore(
    IDatabaseConnectionFactory connections,
    IHttpContextAccessor accessor,
    IApplicationActorContext actor,
    SqlServerActivityStore activity)
    : SqlServerLitigationStoreBase(connections, accessor), IPreFilingMilestoneStore
{
    public string Provider => "SqlServer";

    private const string Columns = """
        id, case_id, milestone, is_marked, occurred_date, marked_at,
        marked_by_user_id, marked_by_display, marked_by_role, note, batch_id, row_version
        """;

    public async Task<List<PreFilingMilestoneRecord>> GetAsync(long? caseId, CancellationToken token = default)
    {
        var result = new List<PreFilingMilestoneRecord>();
        await using var connection = Connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM dbo.case_prefiling_milestones WHERE (@caseId IS NULL OR case_id=@caseId) ORDER BY case_id, id";
        command.Parameters.Add(new SqlParameter("@caseId", (object?)caseId ?? DBNull.Value));
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(Read(reader));
        return result;
    }

    public async Task<PreFilingMilestoneRecord> MarkAsync(long caseId, string milestone, MarkPreFilingMilestoneRequest request, CancellationToken token = default)
    {
        long id;
        await using (var connection = Connections.CreateConnection())
        {
            await connection.OpenAsync(token);
            await using var transaction = await connection.BeginTransactionAsync(token);
            await EnsureCaseExistsAsync(connection, transaction, caseId, token);

            var currentlyMarked = await LoadMarksAsync(connection, transaction, caseId, token);
            PreFilingMilestoneGate.EnsureCanMark(milestone, currentlyMarked);

            long? existingId = null;
            await using (var lookup = connection.CreateCommand())
            {
                lookup.Transaction = transaction;
                lookup.CommandText = "SELECT id FROM dbo.case_prefiling_milestones WHERE case_id=@caseId AND milestone=@milestone";
                lookup.Parameters.Add(new SqlParameter("@caseId", caseId));
                lookup.Parameters.Add(new SqlParameter("@milestone", milestone));
                var value = await lookup.ExecuteScalarAsync(token);
                if (value is not null && value is not DBNull) existingId = Convert.ToInt64(value);
            }

            var now = DateTime.UtcNow.ToString("O");
            if (existingId is null)
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO dbo.case_prefiling_milestones
                        (case_id, milestone, is_marked, occurred_date, marked_at, marked_by_user_id, marked_by_display, marked_by_role, note, batch_id)
                    OUTPUT INSERTED.id
                    VALUES (@caseId, @milestone, 1, @occurredDate, @markedAt, @actorId, @actorDisplay, @actorRole, @note, @batchId)
                    """;
                insert.Parameters.Add(new SqlParameter("@caseId", caseId));
                insert.Parameters.Add(new SqlParameter("@milestone", milestone));
                insert.Parameters.Add(new SqlParameter("@occurredDate", Db(request.OccurredDate)));
                insert.Parameters.Add(new SqlParameter("@markedAt", now));
                insert.Parameters.Add(new SqlParameter("@actorId", (object?)actor.UserId ?? DBNull.Value));
                insert.Parameters.Add(new SqlParameter("@actorDisplay", actor.AuditLabel));
                insert.Parameters.Add(new SqlParameter("@actorRole", Db(actor.Role)));
                insert.Parameters.Add(new SqlParameter("@note", Db(request.Note)));
                insert.Parameters.Add(new SqlParameter("@batchId", Db(request.BatchId)));
                id = Convert.ToInt64(await insert.ExecuteScalarAsync(token));
            }
            else
            {
                id = existingId.Value;
                await using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE dbo.case_prefiling_milestones SET
                        is_marked=1, occurred_date=@occurredDate, marked_at=@markedAt,
                        marked_by_user_id=@actorId, marked_by_display=@actorDisplay,
                        marked_by_role=@actorRole, note=@note, batch_id=@batchId
                    WHERE id=@id
                    """;
                update.Parameters.Add(new SqlParameter("@occurredDate", Db(request.OccurredDate)));
                update.Parameters.Add(new SqlParameter("@markedAt", now));
                update.Parameters.Add(new SqlParameter("@actorId", (object?)actor.UserId ?? DBNull.Value));
                update.Parameters.Add(new SqlParameter("@actorDisplay", actor.AuditLabel));
                update.Parameters.Add(new SqlParameter("@actorRole", Db(actor.Role)));
                update.Parameters.Add(new SqlParameter("@note", Db(request.Note)));
                update.Parameters.Add(new SqlParameter("@batchId", Db(request.BatchId)));
                update.Parameters.Add(new SqlParameter("@id", id));
                await update.ExecuteNonQueryAsync(token);
            }

            await AuditAsync(connection, transaction, caseId, "PreFilingMilestoneMarked", "PreFilingMilestone", id, token);
            await transaction.CommitAsync(token);
        }

        // Same convention as SqlServerSettlementAuthorityRequestStore's actions - the user-facing
        // activity_log write happens as its own call, on its own connection/transaction, after the
        // main insert/update has already committed.
        await activity.RecordAsync(caseId, "PreFilingMilestoneMarked", request.Note, null, token, milestone, "Unmarked", request.OccurredDate);

        var saved = await GetAsync(caseId, token);
        return saved.First(r => r.Id == id);
    }

    public async Task<PreFilingMilestoneRecord> UnmarkAsync(long caseId, string milestone, UnmarkPreFilingMilestoneRequest request, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ArgumentException("A reason is required to unmark a pre-filing milestone.");

        long id;
        string? previousOccurredDate;
        await using (var connection = Connections.CreateConnection())
        {
            await connection.OpenAsync(token);
            await using var transaction = await connection.BeginTransactionAsync(token);

            var currentlyMarked = await LoadMarksAsync(connection, transaction, caseId, token);
            PreFilingMilestoneGate.EnsureCanUnmark(milestone, currentlyMarked);

            await using (var lookup = connection.CreateCommand())
            {
                lookup.Transaction = transaction;
                lookup.CommandText = "SELECT id, occurred_date FROM dbo.case_prefiling_milestones WHERE case_id=@caseId AND milestone=@milestone";
                lookup.Parameters.Add(new SqlParameter("@caseId", caseId));
                lookup.Parameters.Add(new SqlParameter("@milestone", milestone));
                await using var reader = await lookup.ExecuteReaderAsync(token);
                if (!await reader.ReadAsync(token))
                    throw new InvalidOperationException($"\"{PreFilingMilestoneGate.Label(milestone)}\" is not currently marked.");
                id = reader.GetInt64(0);
                previousOccurredDate = Text(reader, 1);
            }

            var now = DateTime.UtcNow.ToString("O");
            await using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE dbo.case_prefiling_milestones SET
                        is_marked=0, occurred_date=NULL, marked_at=@markedAt,
                        marked_by_user_id=@actorId, marked_by_display=@actorDisplay,
                        marked_by_role=@actorRole, note=@note, batch_id=NULL
                    WHERE id=@id
                    """;
                update.Parameters.Add(new SqlParameter("@markedAt", now));
                update.Parameters.Add(new SqlParameter("@actorId", (object?)actor.UserId ?? DBNull.Value));
                update.Parameters.Add(new SqlParameter("@actorDisplay", actor.AuditLabel));
                update.Parameters.Add(new SqlParameter("@actorRole", Db(actor.Role)));
                update.Parameters.Add(new SqlParameter("@note", request.Reason));
                update.Parameters.Add(new SqlParameter("@id", id));
                await update.ExecuteNonQueryAsync(token);
            }

            await AuditAsync(connection, transaction, caseId, "PreFilingMilestoneUnmarked", "PreFilingMilestone", id, token);
            await transaction.CommitAsync(token);
        }

        await activity.RecordAsync(caseId, "PreFilingMilestoneUnmarked", request.Reason, null, token, milestone, previousOccurredDate ?? "Unmarked", "Unmarked");

        var saved = await GetAsync(caseId, token);
        return saved.First(r => r.Id == id);
    }

    public async Task<PreFilingMilestoneAgingSummary> GetAgingAsync(CancellationToken token = default)
    {
        await using var connection = Connections.CreateConnection();
        await connection.OpenAsync(token);

        var cases = new List<(long CaseId, string? JobNumber, string? Tract, string? CaseName)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, job_number, tract, case_name FROM dbo.cases WHERE COALESCE(case_status,'Pipeline')='Pipeline' AND is_deleted=0";
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
                cases.Add((reader.GetInt64(0), Text(reader, 1), Text(reader, 2), Text(reader, 3)));
        }

        var marks = new Dictionary<long, Dictionary<string, string?>>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT case_id, milestone, marked_at FROM dbo.case_prefiling_milestones WHERE is_marked=1";
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                var caseId = reader.GetInt64(0);
                if (!marks.TryGetValue(caseId, out var dict)) marks[caseId] = dict = new();
                dict[reader.GetString(1)] = Text(reader, 2);
            }
        }

        return PreFilingMilestoneGate.BuildAgingSummary(cases, marks);
    }

    private static async Task<Dictionary<string, bool>> LoadMarksAsync(System.Data.Common.DbConnection connection, System.Data.Common.DbTransaction transaction, long caseId, CancellationToken token)
    {
        var marks = new Dictionary<string, bool>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT milestone, is_marked FROM dbo.case_prefiling_milestones WHERE case_id=@caseId";
        command.Parameters.Add(new SqlParameter("@caseId", caseId));
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) marks[reader.GetString(0)] = reader.GetBoolean(1);
        return marks;
    }

    private static PreFilingMilestoneRecord Read(System.Data.Common.DbDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        CaseId = reader.GetInt64(1),
        Milestone = reader.GetString(2),
        IsMarked = reader.GetBoolean(3),
        OccurredDate = Text(reader, 4),
        MarkedAt = Text(reader, 5),
        MarkedByUserId = Text(reader, 6),
        MarkedByDisplay = Text(reader, 7),
        MarkedByRole = Text(reader, 8),
        Note = Text(reader, 9),
        BatchId = Text(reader, 10),
        RowVersion = Convert.ToBase64String((byte[])reader.GetValue(11)),
    };
}
