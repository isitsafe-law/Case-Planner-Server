using System.Data.Common;
using CasePlanner.Data;
using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Security;
using Microsoft.Data.SqlClient;

namespace CasePlanner.Web.Server.Persistence;

public sealed class SqlServerPublicationSummaryStore(
    IDatabaseConnectionFactory connections,
    IApplicationActorContext actor,
    SqlServerActivityStore activity) : IPublicationSummaryStore
{
    public string Provider => "SqlServer";

    public async Task<List<PublicationRecord>> GetAsync(long? caseId, CancellationToken token = default)
    {
        var result = new List<PublicationRecord>();
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT case_id,first_publication_date,second_publication_date,publication_name,
                   marked_perfected,last_updated_at,last_updated_by,row_version
            FROM dbo.case_publications
            WHERE @caseId IS NULL OR case_id=@caseId
            ORDER BY case_id
            """;
        command.Parameters.Add(new SqlParameter("@caseId", (object?)caseId ?? DBNull.Value));
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(Read(reader));
        return result;
    }

    public async Task<PublicationRecord?> GetAsync(long caseId, CancellationToken token = default) =>
        (await GetAsync((long?)caseId, token)).FirstOrDefault();

    public async Task<PublicationRecord> SaveAsync(PublicationRecord model, CancellationToken token = default)
    {
        var first = Date(model.FirstPublicationDate);
        var second = Date(model.SecondPublicationDate);
        if (first is not null && second is not null && DateOnly.Parse(second) < DateOnly.Parse(first))
            throw new InvalidOperationException("Second publication date cannot be earlier than the first publication date.");
        if ((first is not null || second is not null) && string.IsNullOrWhiteSpace(model.PublicationName) && !model.OverrideMissingPublicationName)
            throw new InvalidOperationException("Publication name is required when a publication date is entered. Confirm the override to save without it.");

        var existing = await GetAsync(model.CaseId, token);
        var now = DateTime.UtcNow.ToString("O");
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (existing is null)
        {
            command.CommandText = """
                INSERT INTO dbo.case_publications
                    (case_id,first_publication_date,second_publication_date,publication_name,marked_perfected,
                     last_updated_at,last_updated_by,last_updated_by_user_id)
                OUTPUT INSERTED.row_version
                VALUES(@caseId,@first,@second,@name,@perfected,@at,@by,@actor)
                """;
        }
        else
        {
            command.CommandText = """
                UPDATE dbo.case_publications SET first_publication_date=@first,second_publication_date=@second,
                    publication_name=@name,marked_perfected=@perfected,last_updated_at=@at,last_updated_by=@by,
                    last_updated_by_user_id=@actor
                OUTPUT INSERTED.row_version
                WHERE case_id=@caseId AND row_version=@version
                """;
            command.Parameters.Add(new SqlParameter("@version", ExpectedVersion(model.RowVersion, model.CaseId)));
        }
        command.Parameters.Add(new SqlParameter("@caseId", model.CaseId));
        command.Parameters.Add(new SqlParameter("@first", Db(first)));
        command.Parameters.Add(new SqlParameter("@second", Db(second)));
        command.Parameters.Add(new SqlParameter("@name", Db(model.PublicationName?.Trim())));
        command.Parameters.Add(new SqlParameter("@perfected", model.MarkedPerfected));
        command.Parameters.Add(new SqlParameter("@at", now));
        command.Parameters.Add(new SqlParameter("@by", actor.AuditLabel));
        command.Parameters.Add(new SqlParameter("@actor", (object?)actor.UserId ?? DBNull.Value));
        object? version;
        try { version = await command.ExecuteScalarAsync(token); }
        catch (SqlException ex) when (existing is null && (ex.Number is 2601 or 2627))
        {
            throw new WorkItemConcurrencyException("Publication summary", model.CaseId);
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            throw new InvalidOperationException($"SQL Server case {model.CaseId} does not exist.");
        }
        if (version is null) throw new WorkItemConcurrencyException("Publication summary", model.CaseId);

        await using (var audit = connection.CreateCommand())
        {
            audit.Transaction = transaction;
            audit.CommandText = """
                INSERT INTO dbo.audit_events(case_id,actor_user_id,action,entity_type,entity_id)
                VALUES(@caseId,@actor,@action,'PublicationSummary',@id)
                """;
            audit.Parameters.Add(new SqlParameter("@caseId", model.CaseId));
            audit.Parameters.Add(new SqlParameter("@actor", (object?)actor.UserId ?? DBNull.Value));
            audit.Parameters.Add(new SqlParameter("@action", existing is null ? "PublicationSummaryCreated" : "PublicationSummaryUpdated"));
            audit.Parameters.Add(new SqlParameter("@id", model.CaseId.ToString()));
            await audit.ExecuteNonQueryAsync(token);
        }
        await transaction.CommitAsync(token);

        model.FirstPublicationDate = first;
        model.SecondPublicationDate = second;
        model.PublicationName = string.IsNullOrWhiteSpace(model.PublicationName) ? null : model.PublicationName.Trim();
        model.LastUpdatedAt = now;
        model.LastUpdatedBy = actor.AuditLabel;
        model.RowVersion = Convert.ToBase64String((byte[])version);
        await activity.RecordAsync(model.CaseId, "PublicationChanged",
            $"Publication updated; first {model.FirstPublicationDate ?? "not set"}, second {model.SecondPublicationDate ?? "not set"}, perfected {(model.MarkedPerfected ? "yes" : "no")}",
            null, token);
        return model;
    }

    private static PublicationRecord Read(DbDataReader reader) => new()
    {
        CaseId = reader.GetInt64(0),
        FirstPublicationDate = Date(Text(reader, 1)),
        SecondPublicationDate = Date(Text(reader, 2)),
        PublicationName = Text(reader, 3),
        MarkedPerfected = !reader.IsDBNull(4) && Convert.ToBoolean(reader.GetValue(4)),
        LastUpdatedAt = Text(reader, 5),
        LastUpdatedBy = Text(reader, 6),
        RowVersion = Convert.ToBase64String((byte[])reader.GetValue(7))
    };

    private static byte[] ExpectedVersion(string? value, long caseId)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"RowVersion is required when changing SQL Server publication summary for case {caseId}.");
        try { return Convert.FromBase64String(value); }
        catch (FormatException) { throw new ArgumentException("The publication summary RowVersion is not valid."); }
    }

    private static string? Text(DbDataReader reader, int index) => reader.IsDBNull(index) ? null : Convert.ToString(reader.GetValue(index));
    private static string? Date(string? value) => DateOnly.TryParse(value, out var date) ? date.ToString("yyyy-MM-dd") : null;
    private static object Db(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
}
