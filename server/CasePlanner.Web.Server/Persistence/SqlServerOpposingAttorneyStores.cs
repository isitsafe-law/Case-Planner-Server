using CasePlanner.Data;
using CasePlanner.Web.Server.Models;
using Microsoft.Data.SqlClient;

namespace CasePlanner.Web.Server.Persistence;

// Item 1 (multi-user rollout Phase 2): SQL Server side of the case_opposing_attorneys child
// table, mirroring SqlServerWitnessStore's shape (optimistic concurrency via row_version,
// soft delete via is_deleted, audit trail via dbo.audit_events). There is no live SQL Server
// sandbox available here to exercise this against a real pilot instance - same caveat already
// noted for the rest of the dormant multi-user foundation.
public sealed class SqlServerOpposingAttorneyStore(IDatabaseConnectionFactory connections,IHttpContextAccessor accessor)
    : SqlServerLitigationStoreBase(connections,accessor),IOpposingAttorneyStore
{
    public string Provider=>"SqlServer";
    public async Task<List<OpposingAttorneyRecord>> GetAsync(long? caseId,CancellationToken token=default)
    {
        var result=new List<OpposingAttorneyRecord>();await using var connection=Connections.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();
        command.CommandText="SELECT id,case_id,name,sort_order,row_version FROM dbo.case_opposing_attorneys WHERE is_deleted=0 AND (@caseId IS NULL OR case_id=@caseId) ORDER BY sort_order,id";
        command.Parameters.Add(new SqlParameter("@caseId",(object?)caseId??DBNull.Value));await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token))result.Add(new(){Id=reader.GetInt64(0),CaseId=reader.GetInt64(1),Name=Text(reader,2)??"",SortOrder=reader.IsDBNull(3)?0:Convert.ToInt32(reader.GetValue(3)),RowVersion=Convert.ToBase64String((byte[])reader.GetValue(4))});return result;
    }
    public async Task<OpposingAttorneyRecord> SaveAsync(OpposingAttorneyRecord model,CancellationToken token=default)
    {
        var isNew=model.Id==0;await using var connection=Connections.CreateConnection();await connection.OpenAsync(token);await using var transaction=await connection.BeginTransactionAsync(token);model.CaseId=await ResolveCaseIdAsync(connection,transaction,"case_opposing_attorneys",model.Id,model.CaseId,token);var now=DateTime.UtcNow.ToString("O");await using var command=connection.CreateCommand();command.Transaction=transaction;
        if(isNew)
        {
            await using var nextOrder=connection.CreateCommand();nextOrder.Transaction=transaction;nextOrder.CommandText="SELECT COALESCE(MAX(sort_order),-1)+1 FROM dbo.case_opposing_attorneys WHERE case_id=@caseId AND is_deleted=0";nextOrder.Parameters.Add(new SqlParameter("@caseId",model.CaseId));
            model.SortOrder=Convert.ToInt32(await nextOrder.ExecuteScalarAsync(token));
            command.CommandText="INSERT INTO dbo.case_opposing_attorneys (case_id,name,sort_order,created_at,updated_at) OUTPUT INSERTED.id,INSERTED.row_version VALUES (@caseId,@name,@sortOrder,@now,@now)";
        }
        else{command.CommandText="UPDATE dbo.case_opposing_attorneys SET name=@name,sort_order=@sortOrder,updated_at=@now OUTPUT INSERTED.id,INSERTED.row_version WHERE id=@id AND row_version=@version AND is_deleted=0";command.Parameters.Add(new SqlParameter("@id",model.Id));command.Parameters.Add(new SqlParameter("@version",ExpectedVersion(model.RowVersion,"opposing attorney",model.Id)));}
        command.Parameters.Add(new SqlParameter("@caseId",model.CaseId));command.Parameters.Add(new SqlParameter("@name",model.Name??""));command.Parameters.Add(new SqlParameter("@sortOrder",model.SortOrder));command.Parameters.Add(new SqlParameter("@now",now));
        await using(var reader=await command.ExecuteReaderAsync(token)){if(!await reader.ReadAsync(token))throw new WorkItemConcurrencyException("Opposing attorney",model.Id);model.Id=reader.GetInt64(0);model.RowVersion=Convert.ToBase64String((byte[])reader.GetValue(1));}
        await AuditAsync(connection,transaction,model.CaseId,isNew?"OpposingAttorneyCreated":"OpposingAttorneyUpdated","OpposingAttorney",model.Id,token);await transaction.CommitAsync(token);return model;
    }
    public Task DeleteAsync(long id,string? rowVersion=null,CancellationToken token=default)=>SoftDeleteAsync("case_opposing_attorneys","Opposing attorney","OpposingAttorney",id,rowVersion,token);
}
