using CasePlanner.Data;
using CasePlanner.Web.Server.Models;
using Microsoft.Data.SqlClient;

namespace CasePlanner.Web.Server.Persistence;

// Test-build feedback item: SQL Server side of the case_legal_assistants child table, mirroring
// SqlServerOpposingAttorneyStore's shape (optimistic concurrency via row_version, soft delete via
// is_deleted, audit trail via dbo.audit_events). There is no live SQL Server sandbox available
// here to exercise this against a real pilot instance - same caveat already noted for the rest of
// the dormant multi-user foundation.
public sealed class SqlServerCaseLegalAssistantStore(IDatabaseConnectionFactory connections,IHttpContextAccessor accessor)
    : SqlServerLitigationStoreBase(connections,accessor),ICaseLegalAssistantStore
{
    public string Provider=>"SqlServer";
    public async Task<List<CaseLegalAssistantRecord>> GetAsync(long? caseId,CancellationToken token=default)
    {
        var result=new List<CaseLegalAssistantRecord>();await using var connection=Connections.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();
        command.CommandText="SELECT id,case_id,name,sort_order,row_version FROM dbo.case_legal_assistants WHERE is_deleted=0 AND (@caseId IS NULL OR case_id=@caseId) ORDER BY sort_order,id";
        command.Parameters.Add(new SqlParameter("@caseId",(object?)caseId??DBNull.Value));await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token))result.Add(new(){Id=reader.GetInt64(0),CaseId=reader.GetInt64(1),Name=Text(reader,2)??"",SortOrder=reader.IsDBNull(3)?0:Convert.ToInt32(reader.GetValue(3)),RowVersion=Convert.ToBase64String((byte[])reader.GetValue(4))});return result;
    }
    public async Task<CaseLegalAssistantRecord> SaveAsync(CaseLegalAssistantRecord model,CancellationToken token=default)
    {
        var isNew=model.Id==0;await using var connection=Connections.CreateConnection();await connection.OpenAsync(token);await using var transaction=await connection.BeginTransactionAsync(token);model.CaseId=await ResolveCaseIdAsync(connection,transaction,"case_legal_assistants",model.Id,model.CaseId,token);var now=DateTime.UtcNow.ToString("O");await using var command=connection.CreateCommand();command.Transaction=transaction;
        if(isNew)
        {
            await using var nextOrder=connection.CreateCommand();nextOrder.Transaction=transaction;nextOrder.CommandText="SELECT COALESCE(MAX(sort_order),-1)+1 FROM dbo.case_legal_assistants WHERE case_id=@caseId AND is_deleted=0";nextOrder.Parameters.Add(new SqlParameter("@caseId",model.CaseId));
            model.SortOrder=Convert.ToInt32(await nextOrder.ExecuteScalarAsync(token));
            command.CommandText="INSERT INTO dbo.case_legal_assistants (case_id,name,sort_order,created_at,updated_at) OUTPUT INSERTED.id,INSERTED.row_version VALUES (@caseId,@name,@sortOrder,@now,@now)";
        }
        else{command.CommandText="UPDATE dbo.case_legal_assistants SET name=@name,sort_order=@sortOrder,updated_at=@now OUTPUT INSERTED.id,INSERTED.row_version WHERE id=@id AND row_version=@version AND is_deleted=0";command.Parameters.Add(new SqlParameter("@id",model.Id));command.Parameters.Add(new SqlParameter("@version",ExpectedVersion(model.RowVersion,"legal assistant",model.Id)));}
        command.Parameters.Add(new SqlParameter("@caseId",model.CaseId));command.Parameters.Add(new SqlParameter("@name",model.Name??""));command.Parameters.Add(new SqlParameter("@sortOrder",model.SortOrder));command.Parameters.Add(new SqlParameter("@now",now));
        await using(var reader=await command.ExecuteReaderAsync(token)){if(!await reader.ReadAsync(token))throw new WorkItemConcurrencyException("Legal assistant",model.Id);model.Id=reader.GetInt64(0);model.RowVersion=Convert.ToBase64String((byte[])reader.GetValue(1));}
        await AuditAsync(connection,transaction,model.CaseId,isNew?"CaseLegalAssistantCreated":"CaseLegalAssistantUpdated","CaseLegalAssistant",model.Id,token);await transaction.CommitAsync(token);return model;
    }
    public Task DeleteAsync(long id,string? rowVersion=null,CancellationToken token=default)=>SoftDeleteAsync("case_legal_assistants","Legal assistant","CaseLegalAssistant",id,rowVersion,token);
}
