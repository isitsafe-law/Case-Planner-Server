using CasePlanner.Data;
using CasePlanner.Web.Server.Models;
using Microsoft.Data.SqlClient;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

public interface ICaseAttorneyAssignmentStore
{
    string Provider { get; }
    Task<List<CaseAttorneyAssignmentRecord>> GetAsync(long? caseId, CancellationToken token = default);
    Task<CaseAttorneyAssignmentRecord> SaveAsync(CaseAttorneyAssignmentRecord model, CancellationToken token = default);
    Task DeleteAsync(long id, CancellationToken token = default);
}

public sealed class SqliteCaseAttorneyAssignmentStore(CasePlannerRepository repository) : ICaseAttorneyAssignmentStore
{
    public string Provider => "Sqlite";
    public Task<List<CaseAttorneyAssignmentRecord>> GetAsync(long? caseId, CancellationToken token = default) => repository.GetCaseAttorneyAssignmentsAsync(caseId);
    public Task<CaseAttorneyAssignmentRecord> SaveAsync(CaseAttorneyAssignmentRecord model, CancellationToken token = default) => repository.SaveCaseAttorneyAssignmentAsync(model);
    public Task DeleteAsync(long id, CancellationToken token = default) => repository.DeleteCaseAttorneyAssignmentAsync(id);
}

public sealed class SqlServerCaseAttorneyAssignmentStore(IDatabaseConnectionFactory connections, IHttpContextAccessor accessor)
    : SqlServerLitigationStoreBase(connections, accessor), ICaseAttorneyAssignmentStore
{
    public string Provider => "SqlServer";
    public async Task<List<CaseAttorneyAssignmentRecord>> GetAsync(long? caseId, CancellationToken token = default)
    {
        var result = new List<CaseAttorneyAssignmentRecord>();
        await using var connection = Connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,case_id,name,role,sort_order,row_version FROM dbo.case_attorney_assignments WHERE is_deleted=0 AND (@caseId IS NULL OR case_id=@caseId) ORDER BY sort_order,id";
        command.Parameters.Add(new SqlParameter("@caseId", (object?)caseId ?? DBNull.Value));
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            result.Add(new CaseAttorneyAssignmentRecord { Id = reader.GetInt64(0), CaseId = reader.GetInt64(1), Name = Text(reader, 2) ?? "", Role = Text(reader, 3) ?? "Supporting", SortOrder = Convert.ToInt32(reader.GetValue(4)), RowVersion = Convert.ToBase64String((byte[])reader.GetValue(5)) });
        return result;
    }

    public async Task<CaseAttorneyAssignmentRecord> SaveAsync(CaseAttorneyAssignmentRecord model, CancellationToken token = default)
    {
        var isNew = model.Id == 0;
        await using var connection = Connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        model.CaseId = await ResolveCaseIdAsync(connection, transaction, "case_attorney_assignments", model.Id, model.CaseId, token);
        model.Role = string.Equals(model.Role, "Primary", StringComparison.OrdinalIgnoreCase) ? "Primary" : "Supporting";
        var now = DateTime.UtcNow.ToString("O");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (isNew)
        {
            await using var next = connection.CreateCommand();
            next.Transaction = transaction;
            next.CommandText = "SELECT COALESCE(MAX(sort_order),-1)+1 FROM dbo.case_attorney_assignments WHERE case_id=@caseId AND is_deleted=0";
            next.Parameters.Add(new SqlParameter("@caseId", model.CaseId));
            model.SortOrder = Convert.ToInt32(await next.ExecuteScalarAsync(token));
            command.CommandText = "INSERT INTO dbo.case_attorney_assignments (case_id,name,role,sort_order,created_at,updated_at) OUTPUT INSERTED.id,INSERTED.row_version VALUES (@caseId,@name,@role,@sortOrder,@now,@now)";
        }
        else
        {
            command.CommandText = "UPDATE dbo.case_attorney_assignments SET name=@name,role=@role,sort_order=@sortOrder,updated_at=@now OUTPUT INSERTED.id,INSERTED.row_version WHERE id=@id AND row_version=@version AND is_deleted=0";
            command.Parameters.Add(new SqlParameter("@id", model.Id));
            command.Parameters.Add(new SqlParameter("@version", ExpectedVersion(model.RowVersion, "case attorney assignment", model.Id)));
        }
        command.Parameters.Add(new SqlParameter("@caseId", model.CaseId));
        command.Parameters.Add(new SqlParameter("@name", model.Name ?? ""));
        command.Parameters.Add(new SqlParameter("@role", model.Role));
        command.Parameters.Add(new SqlParameter("@sortOrder", model.SortOrder));
        command.Parameters.Add(new SqlParameter("@now", now));
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) throw new WorkItemConcurrencyException("Case attorney assignment", model.Id);
            model.Id = reader.GetInt64(0);
            model.RowVersion = Convert.ToBase64String((byte[])reader.GetValue(1));
        }
        await AuditAsync(connection, transaction, model.CaseId, isNew ? "CaseAttorneyAssignmentCreated" : "CaseAttorneyAssignmentUpdated", "CaseAttorneyAssignment", model.Id, token);
        await transaction.CommitAsync(token);
        return model;
    }

    public Task DeleteAsync(long id, CancellationToken token = default) => SoftDeleteAsync("case_attorney_assignments", "Case attorney assignment", "CaseAttorneyAssignment", id, null, token);
}
