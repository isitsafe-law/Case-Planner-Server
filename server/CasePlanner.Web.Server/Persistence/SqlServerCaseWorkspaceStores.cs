using System.Data.Common;
using CasePlanner.Data;
using CasePlanner.Web.Server.Models;
using Microsoft.Data.SqlClient;

namespace CasePlanner.Web.Server.Persistence;

public sealed class SqlServerCaseNoteStore(IDatabaseConnectionFactory connections, IHttpContextAccessor accessor)
    : SqlServerWorkItemStoreBase(connections, accessor), ICaseNoteStore
{
    public string Provider => "SqlServer";

    public async Task<List<CaseNoteRecord>> GetAsync(long? caseId, CancellationToken token = default)
    {
        var result = new List<CaseNoteRecord>();
        await using var connection = Connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,case_id,title,body,created_at,updated_at,row_version
            FROM dbo.case_notes
            WHERE is_deleted=0 AND (@caseId IS NULL OR case_id=@caseId)
            ORDER BY updated_at DESC,id DESC
            """;
        command.Parameters.Add(new SqlParameter("@caseId", (object?)caseId ?? DBNull.Value));
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(Read(reader));
        return result;
    }

    public async Task<CaseNoteRecord> SaveAsync(CaseNoteRecord model, CancellationToken token = default)
    {
        var isNew = model.Id == 0;
        await using var connection = Connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        if (isNew) await EnsureCaseExistsAsync(connection, transaction, model.CaseId, token);
        else model.CaseId = await GetStoredCaseIdAsync(connection, transaction, "case_notes", model.Id, token) ?? model.CaseId;

        var now = DateTime.UtcNow.ToString("O");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (isNew)
        {
            command.CommandText = """
                INSERT INTO dbo.case_notes (case_id,title,body,created_at,updated_at)
                OUTPUT INSERTED.id,INSERTED.row_version,INSERTED.created_at,INSERTED.updated_at
                VALUES (@caseId,@title,@body,@now,@now)
                """;
        }
        else
        {
            command.CommandText = """
                UPDATE dbo.case_notes SET title=@title,body=@body,updated_at=@now
                OUTPUT INSERTED.id,INSERTED.row_version,INSERTED.created_at,INSERTED.updated_at
                WHERE id=@id AND row_version=@version AND is_deleted=0
                """;
            command.Parameters.Add(new SqlParameter("@id", model.Id));
            command.Parameters.Add(new SqlParameter("@version", ExpectedVersion(model.RowVersion, "case note", model.Id)));
        }
        command.Parameters.Add(new SqlParameter("@caseId", model.CaseId));
        command.Parameters.Add(new SqlParameter("@title", string.IsNullOrWhiteSpace(model.Title) ? "Untitled Note" : model.Title.Trim()));
        command.Parameters.Add(new SqlParameter("@body", model.Body ?? ""));
        command.Parameters.Add(new SqlParameter("@now", now));
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) throw new WorkItemConcurrencyException("Case note", model.Id);
            model.Id = reader.GetInt64(0);
            model.RowVersion = Convert.ToBase64String((byte[])reader.GetValue(1));
            model.CreatedAt = Text(reader, 2) ?? "";
            model.UpdatedAt = Text(reader, 3) ?? "";
        }
        await AuditAsync(connection, transaction, model.CaseId, isNew ? "CaseNoteCreated" : "CaseNoteUpdated", "CaseNote", model.Id, token);
        await transaction.CommitAsync(token);
        return model;
    }

    public async Task DeleteAsync(long id, string? rowVersion = null, CancellationToken token = default) =>
        await SoftDeleteAsync("case_notes", "Case note", "CaseNote", "CaseNoteDeleted", id, rowVersion, token);

    private static CaseNoteRecord Read(DbDataReader reader) => new()
    {
        Id = reader.GetInt64(0), CaseId = reader.GetInt64(1), Title = Text(reader, 2) ?? "", Body = Text(reader, 3) ?? "",
        CreatedAt = Text(reader, 4) ?? "", UpdatedAt = Text(reader, 5) ?? "", RowVersion = Convert.ToBase64String((byte[])reader.GetValue(6))
    };

    private async Task<long?> GetStoredCaseIdAsync(DbConnection connection, DbTransaction transaction, string table, long id, CancellationToken token)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT case_id FROM dbo.{table} WHERE id=@id AND is_deleted=0";
        command.Parameters.Add(new SqlParameter("@id", id));
        var result = await command.ExecuteScalarAsync(token);
        return result is null ? null : Convert.ToInt64(result);
    }

    private async Task SoftDeleteAsync(string table, string kind, string entityType, string action, long id, string? rowVersion, CancellationToken token)
    {
        await using var connection = Connections.CreateConnection(); await connection.OpenAsync(token); await using var transaction = await connection.BeginTransactionAsync(token);
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"UPDATE dbo.{table} SET is_deleted=1,deleted_utc=SYSUTCDATETIME(),deleted_by_user_id=@actor OUTPUT INSERTED.case_id WHERE id=@id AND row_version=@version AND is_deleted=0";
        command.Parameters.Add(new SqlParameter("@actor", (object?)ActorUserId ?? DBNull.Value)); command.Parameters.Add(new SqlParameter("@id", id)); command.Parameters.Add(new SqlParameter("@version", ExpectedVersion(rowVersion, kind.ToLowerInvariant(), id)));
        var caseId = await command.ExecuteScalarAsync(token); if (caseId is null) throw new WorkItemConcurrencyException(kind, id);
        await AuditAsync(connection, transaction, Convert.ToInt64(caseId), action, entityType, id, token); await transaction.CommitAsync(token);
    }
}

public sealed class SqlServerHearingStore(IDatabaseConnectionFactory connections, IHttpContextAccessor accessor)
    : SqlServerWorkItemStoreBase(connections, accessor), IHearingStore
{
    public string Provider => "SqlServer";

    public async Task<List<HearingRecord>> GetAsync(long? caseId, CancellationToken token = default)
    {
        var result = new List<HearingRecord>();
        await using var connection = Connections.CreateConnection(); await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,case_id,title,hearing_date,location,description,created_at,updated_at,row_version
            FROM dbo.hearings WHERE is_deleted=0 AND (@caseId IS NULL OR case_id=@caseId)
            ORDER BY COALESCE(hearing_date,'9999-12-31'),id
            """;
        command.Parameters.Add(new SqlParameter("@caseId", (object?)caseId ?? DBNull.Value));
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(Read(reader));
        return result;
    }

    public async Task<HearingRecord> SaveAsync(HearingRecord model, CancellationToken token = default)
    {
        var isNew = model.Id == 0;
        await using var connection = Connections.CreateConnection(); await connection.OpenAsync(token); await using var transaction = await connection.BeginTransactionAsync(token);
        if (isNew) await EnsureCaseExistsAsync(connection, transaction, model.CaseId, token);
        else
        {
            await using var lookup = connection.CreateCommand(); lookup.Transaction = transaction;
            lookup.CommandText = "SELECT case_id FROM dbo.hearings WHERE id=@id AND is_deleted=0"; lookup.Parameters.Add(new SqlParameter("@id", model.Id));
            var stored = await lookup.ExecuteScalarAsync(token); if (stored is not null) model.CaseId = Convert.ToInt64(stored);
        }
        var now = DateTime.UtcNow.ToString("O"); await using var command = connection.CreateCommand(); command.Transaction = transaction;
        if (isNew) command.CommandText = """
            INSERT INTO dbo.hearings (case_id,title,hearing_date,location,description,created_at,updated_at)
            OUTPUT INSERTED.id,INSERTED.row_version,INSERTED.created_at,INSERTED.updated_at VALUES (@caseId,@title,@date,@location,@description,@now,@now)
            """;
        else
        {
            command.CommandText = """
                UPDATE dbo.hearings SET title=@title,hearing_date=@date,location=@location,description=@description,updated_at=@now
                OUTPUT INSERTED.id,INSERTED.row_version,INSERTED.created_at,INSERTED.updated_at
                WHERE id=@id AND row_version=@version AND is_deleted=0
                """;
            command.Parameters.Add(new SqlParameter("@id", model.Id)); command.Parameters.Add(new SqlParameter("@version", ExpectedVersion(model.RowVersion, "hearing", model.Id)));
        }
        command.Parameters.Add(new SqlParameter("@caseId", model.CaseId)); command.Parameters.Add(new SqlParameter("@title", string.IsNullOrWhiteSpace(model.Title) ? "Hearing" : model.Title.Trim()));
        command.Parameters.Add(new SqlParameter("@date", Db(Date(model.HearingDate)))); command.Parameters.Add(new SqlParameter("@location", Db(model.Location))); command.Parameters.Add(new SqlParameter("@description", Db(model.Description))); command.Parameters.Add(new SqlParameter("@now", now));
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) throw new WorkItemConcurrencyException("Hearing", model.Id);
            model.Id=reader.GetInt64(0); model.RowVersion=Convert.ToBase64String((byte[])reader.GetValue(1)); model.CreatedAt=Text(reader,2)??""; model.UpdatedAt=Text(reader,3)??"";
        }
        await AuditAsync(connection, transaction, model.CaseId, isNew ? "HearingCreated" : "HearingUpdated", "Hearing", model.Id, token); await transaction.CommitAsync(token); return model;
    }

    public async Task DeleteAsync(long id, string? rowVersion = null, CancellationToken token = default)
    {
        await using var connection = Connections.CreateConnection(); await connection.OpenAsync(token); await using var transaction = await connection.BeginTransactionAsync(token);
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "UPDATE dbo.hearings SET is_deleted=1,deleted_utc=SYSUTCDATETIME(),deleted_by_user_id=@actor OUTPUT INSERTED.case_id WHERE id=@id AND row_version=@version AND is_deleted=0";
        command.Parameters.Add(new SqlParameter("@actor", (object?)ActorUserId ?? DBNull.Value)); command.Parameters.Add(new SqlParameter("@id", id)); command.Parameters.Add(new SqlParameter("@version", ExpectedVersion(rowVersion, "hearing", id)));
        var caseId=await command.ExecuteScalarAsync(token); if(caseId is null) throw new WorkItemConcurrencyException("Hearing",id);
        await AuditAsync(connection,transaction,Convert.ToInt64(caseId),"HearingDeleted","Hearing",id,token); await transaction.CommitAsync(token);
    }

    private static HearingRecord Read(DbDataReader reader) => new()
    {
        Id=reader.GetInt64(0),CaseId=reader.GetInt64(1),Title=Text(reader,2)??"",HearingDate=Date(Text(reader,3)),Location=Text(reader,4),Description=Text(reader,5),
        CreatedAt=Text(reader,6)??"",UpdatedAt=Text(reader,7)??"",RowVersion=Convert.ToBase64String((byte[])reader.GetValue(8))
    };
}
