using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;
using CasePlanner.Data;

namespace CasePlanner.Web.Server.Persistence;

public interface IServiceLogStore
{
    Task<List<ServiceLogEntry>> GetAsync(long caseId, CancellationToken token = default);
    Task<ServiceLogEntry> SaveAsync(ServiceLogEntry model, CancellationToken token = default);
    Task DeleteAsync(long id, CancellationToken token = default);
}

public sealed class SqliteServiceLogStore(CasePlannerRepository repository) : IServiceLogStore
{
    public Task<List<ServiceLogEntry>> GetAsync(long caseId, CancellationToken token = default) =>
        repository.GetServiceLogEntriesAsync(caseId);

    public Task<ServiceLogEntry> SaveAsync(ServiceLogEntry model, CancellationToken token = default) =>
        repository.SaveServiceLogEntryAsync(model);

    public Task DeleteAsync(long id, CancellationToken token = default) =>
        repository.DeleteServiceLogEntryAsync(id);
}

public sealed class SqlServerServiceLogStore(IDatabaseConnectionFactory connections, IHttpContextAccessor accessor)
    : SqlServerLitigationStoreBase(connections, accessor), IServiceLogStore
{
    public async Task<List<ServiceLogEntry>> GetAsync(long caseId, CancellationToken token = default)
    {
        var result = new List<ServiceLogEntry>();
        await using var connection = Connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,case_id,case_defendant_id,party_name,status,method,event_date,notes,created_at,updated_at,row_version FROM dbo.service_log_entries WHERE is_deleted=0 AND case_id=@caseId ORDER BY party_name,COALESCE(event_date,'9999-12-31')";
        command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@caseId", caseId));
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            result.Add(new ServiceLogEntry { Id = reader.GetInt64(0), CaseId = reader.GetInt64(1), CaseDefendantId = reader.IsDBNull(2) ? null : reader.GetInt64(2), PartyName = Text(reader, 3) ?? "", Status = Text(reader, 4) ?? "Not Served", Method = Text(reader, 5), EventDate = Text(reader, 6), Notes = Text(reader, 7), CreatedAt = Text(reader, 8), UpdatedAt = Text(reader, 9), RowVersion = Convert.ToBase64String((byte[])reader.GetValue(10)) });
        return result;
    }

    public async Task<ServiceLogEntry> SaveAsync(ServiceLogEntry model, CancellationToken token = default)
    {
        var isNew = model.Id == 0;
        await using var connection = Connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        model.CaseId = await ResolveCaseIdAsync(connection, transaction, "service_log_entries", model.Id, model.CaseId, token);
        if (model.CaseDefendantId is { } defendantId)
        {
            await using var party = connection.CreateCommand();
            party.Transaction = transaction;
            party.CommandText = "SELECT name FROM dbo.case_defendants WHERE id=@id AND case_id=@caseId AND is_deleted=0";
            party.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@id", defendantId));
            party.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@caseId", model.CaseId));
            var name = await party.ExecuteScalarAsync(token);
            if (name is null) throw new InvalidOperationException("The selected service party does not belong to this case.");
            model.PartyName = Convert.ToString(name) ?? model.PartyName;
        }
        var now = DateTime.UtcNow.ToString("O");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (isNew)
            command.CommandText = "INSERT INTO dbo.service_log_entries (case_id,case_defendant_id,party_name,status,method,event_date,notes,created_at,updated_at) OUTPUT INSERTED.id,INSERTED.row_version VALUES (@caseId,@defendantId,@partyName,@status,@method,@eventDate,@notes,@now,@now)";
        else
        {
            command.CommandText = "UPDATE dbo.service_log_entries SET case_defendant_id=@defendantId,party_name=@partyName,status=@status,method=@method,event_date=@eventDate,notes=@notes,updated_at=@now OUTPUT INSERTED.id,INSERTED.row_version WHERE id=@id AND row_version=@version AND is_deleted=0";
            command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@id", model.Id));
            command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@version", ExpectedVersion(model.RowVersion, "service log entry", model.Id)));
        }
        command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@caseId", model.CaseId));
        command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@defendantId", (object?)model.CaseDefendantId ?? DBNull.Value));
        command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@partyName", model.PartyName ?? ""));
        command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@status", model.Status ?? "Not Served"));
        command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@method", (object?)model.Method ?? DBNull.Value));
        command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@eventDate", (object?)model.EventDate ?? DBNull.Value));
        command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@notes", (object?)model.Notes ?? DBNull.Value));
        command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@now", now));
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) throw new WorkItemConcurrencyException("Service log entry", model.Id);
            model.Id = reader.GetInt64(0);
            model.RowVersion = Convert.ToBase64String((byte[])reader.GetValue(1));
        }
        await AuditAsync(connection, transaction, model.CaseId, isNew ? "ServiceLogCreated" : "ServiceLogUpdated", "ServiceLogEntry", model.Id, token);
        await transaction.CommitAsync(token);
        return model;
    }

    public async Task DeleteAsync(long id, CancellationToken token = default)
    {
        await using var connection = Connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE dbo.service_log_entries SET is_deleted=1,deleted_utc=SYSUTCDATETIME(),deleted_by_user_id=@actor WHERE id=@id AND is_deleted=0";
        command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@actor", (object?)ActorUserId ?? DBNull.Value));
        command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@id", id));
        await command.ExecuteNonQueryAsync(token);
        await transaction.CommitAsync(token);
    }
}
