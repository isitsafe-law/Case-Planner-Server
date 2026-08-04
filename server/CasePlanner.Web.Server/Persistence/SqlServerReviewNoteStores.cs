using CasePlanner.Data;
using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Security;
using Microsoft.Data.SqlClient;

namespace CasePlanner.Web.Server.Persistence;

// SQL Server side of the review-note log (see ReviewNoteStores.cs for the shared rationale).
// dbo.case_review_notes (063_case_review_notes.sql) is append-only - no row_version, no update, no
// delete - so this needs only EnsureCaseExistsAsync/AuditAsync (via SqlServerLitigationStoreBase),
// plus a SqlServerActivityStore dependency to write the user-facing activity_log entry after each
// note. There is no live SQL Server sandbox available here to exercise this against a real pilot
// instance - same caveat already noted for the rest of the dormant multi-user foundation.
public sealed class SqlServerReviewNoteStore(
    IDatabaseConnectionFactory connections,
    IHttpContextAccessor accessor,
    IApplicationActorContext actor,
    SqlServerActivityStore activity)
    : SqlServerLitigationStoreBase(connections, accessor), IReviewNoteStore
{
    public string Provider => "SqlServer";

    private const string Columns = """
        id, case_id, reviewer_name, reviewer_role, decision, comment, occurred_date,
        created_at, created_by_user_id, created_by_display, created_by_role
        """;

    public async Task<List<ReviewNoteRecord>> GetAsync(long? caseId, CancellationToken token = default)
    {
        var result = new List<ReviewNoteRecord>();
        await using var connection = Connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM dbo.case_review_notes WHERE (@caseId IS NULL OR case_id=@caseId) ORDER BY occurred_date, id";
        command.Parameters.Add(new SqlParameter("@caseId", (object?)caseId ?? DBNull.Value));
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(Read(reader));
        return result;
    }

    public async Task<ReviewNoteRecord> CreateAsync(long caseId, CreateReviewNoteRequest request, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(request.Decision))
            throw new ArgumentException("A decision (e.g. \"Looks good\" or \"Sent back for revision\") is required.");

        long id;
        var now = DateTime.UtcNow.ToString("O");
        var occurredDate = string.IsNullOrWhiteSpace(request.OccurredDate) ? now[..10] : request.OccurredDate;
        await using (var connection = Connections.CreateConnection())
        {
            await connection.OpenAsync(token);
            await using var transaction = await connection.BeginTransactionAsync(token);
            await EnsureCaseExistsAsync(connection, transaction, caseId, token);

            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO dbo.case_review_notes
                        (case_id, reviewer_name, reviewer_role, decision, comment, occurred_date,
                         created_at, created_by_user_id, created_by_display, created_by_role)
                    OUTPUT INSERTED.id
                    VALUES (@caseId, @reviewerName, @reviewerRole, @decision, @comment, @occurredDate,
                            @createdAt, @actorId, @actorDisplay, @actorRole)
                    """;
                insert.Parameters.Add(new SqlParameter("@caseId", caseId));
                insert.Parameters.Add(new SqlParameter("@reviewerName", Db(request.ReviewerName)));
                insert.Parameters.Add(new SqlParameter("@reviewerRole", Db(request.ReviewerRole)));
                insert.Parameters.Add(new SqlParameter("@decision", request.Decision.Trim()));
                insert.Parameters.Add(new SqlParameter("@comment", Db(request.Comment)));
                insert.Parameters.Add(new SqlParameter("@occurredDate", occurredDate));
                insert.Parameters.Add(new SqlParameter("@createdAt", now));
                insert.Parameters.Add(new SqlParameter("@actorId", (object?)actor.UserId ?? DBNull.Value));
                insert.Parameters.Add(new SqlParameter("@actorDisplay", actor.AuditLabel));
                insert.Parameters.Add(new SqlParameter("@actorRole", Db(actor.Role)));
                id = Convert.ToInt64(await insert.ExecuteScalarAsync(token));
            }

            await AuditAsync(connection, transaction, caseId, "ReviewNoteAdded", "ReviewNote", id, token);
            await transaction.CommitAsync(token);
        }

        // Audit write happens as its own call, after the main insert has already committed - same
        // convention used throughout the SQL Server pilot stores.
        await activity.RecordAsync(caseId, "ReviewNoteAdded",
            string.IsNullOrWhiteSpace(request.Comment) ? request.Decision.Trim() : $"{request.Decision.Trim()} — {request.Comment.Trim()}",
            null, token);

        var created = await GetAsync(caseId, token);
        return created.First(r => r.Id == id);
    }

    private static ReviewNoteRecord Read(System.Data.Common.DbDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        CaseId = reader.GetInt64(1),
        ReviewerName = Text(reader, 2),
        ReviewerRole = Text(reader, 3),
        Decision = Text(reader, 4) ?? "",
        Comment = Text(reader, 5),
        OccurredDate = Text(reader, 6) ?? "",
        CreatedAt = Text(reader, 7),
        CreatedByUserId = Text(reader, 8),
        CreatedByDisplay = Text(reader, 9),
        CreatedByRole = Text(reader, 10),
    };
}
