using System.Data.Common;
using CasePlanner.Data;
using CasePlanner.Web.Server.Models;
using Microsoft.Data.SqlClient;

namespace CasePlanner.Web.Server.Persistence;

public abstract class SqlServerLitigationStoreBase(IDatabaseConnectionFactory connections, IHttpContextAccessor accessor)
    : SqlServerWorkItemStoreBase(connections, accessor)
{
    protected async Task<long> ResolveCaseIdAsync(DbConnection connection, DbTransaction transaction, string table, long id, long submittedCaseId, CancellationToken token)
    {
        if (id == 0) { await EnsureCaseExistsAsync(connection, transaction, submittedCaseId, token); return submittedCaseId; }
        await using var command=connection.CreateCommand(); command.Transaction=transaction; command.CommandText=$"SELECT case_id FROM dbo.{table} WHERE id=@id AND is_deleted=0"; command.Parameters.Add(new SqlParameter("@id",id));
        var result=await command.ExecuteScalarAsync(token); return result is null ? submittedCaseId : Convert.ToInt64(result);
    }

    protected async Task SoftDeleteAsync(string table,string kind,string entityType,long id,string? rowVersion,CancellationToken token)
    {
        await using var connection=Connections.CreateConnection();await connection.OpenAsync(token);await using var transaction=await connection.BeginTransactionAsync(token);await using var command=connection.CreateCommand();command.Transaction=transaction;
        command.CommandText=$"UPDATE dbo.{table} SET is_deleted=1,deleted_utc=SYSUTCDATETIME(),deleted_by_user_id=@actor OUTPUT INSERTED.case_id WHERE id=@id AND row_version=@version AND is_deleted=0";
        command.Parameters.Add(new SqlParameter("@actor",(object?)ActorUserId??DBNull.Value));command.Parameters.Add(new SqlParameter("@id",id));command.Parameters.Add(new SqlParameter("@version",ExpectedVersion(rowVersion,kind.ToLowerInvariant(),id)));
        var caseId=await command.ExecuteScalarAsync(token);if(caseId is null)throw new WorkItemConcurrencyException(kind,id);
        await AuditAsync(connection,transaction,Convert.ToInt64(caseId),$"{entityType}Deleted",entityType,id,token);await transaction.CommitAsync(token);
    }
}

public sealed class SqlServerWitnessStore(IDatabaseConnectionFactory connections,IHttpContextAccessor accessor)
    : SqlServerLitigationStoreBase(connections,accessor),IWitnessStore
{
    public string Provider=>"SqlServer";
    public async Task<List<WitnessRecord>> GetAsync(long? caseId,CancellationToken token=default)
    {
        var result=new List<WitnessRecord>();await using var connection=Connections.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();
        command.CommandText="SELECT id,case_id,name,side,role,contact_info,subpoena_status,outline_notes,notes,row_version FROM dbo.witnesses WHERE is_deleted=0 AND (@caseId IS NULL OR case_id=@caseId) ORDER BY side,name";
        command.Parameters.Add(new SqlParameter("@caseId",(object?)caseId??DBNull.Value));await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token))result.Add(new(){Id=reader.GetInt64(0),CaseId=reader.GetInt64(1),Name=Text(reader,2)??"",Side=Text(reader,3)??"ASHC",Role=Text(reader,4),ContactInfo=Text(reader,5),SubpoenaStatus=Text(reader,6)??"Not Needed",OutlineNotes=Text(reader,7),Notes=Text(reader,8),RowVersion=Convert.ToBase64String((byte[])reader.GetValue(9))});return result;
    }
    public async Task<WitnessRecord> SaveAsync(WitnessRecord model,CancellationToken token=default)
    {
        var isNew=model.Id==0;await using var connection=Connections.CreateConnection();await connection.OpenAsync(token);await using var transaction=await connection.BeginTransactionAsync(token);model.CaseId=await ResolveCaseIdAsync(connection,transaction,"witnesses",model.Id,model.CaseId,token);var now=DateTime.UtcNow.ToString("O");await using var command=connection.CreateCommand();command.Transaction=transaction;
        if(isNew)command.CommandText="INSERT INTO dbo.witnesses (case_id,name,side,role,contact_info,subpoena_status,outline_notes,notes,created_at,updated_at) OUTPUT INSERTED.id,INSERTED.row_version VALUES (@caseId,@name,@side,@role,@contact,@subpoena,@outline,@notes,@now,@now)";
        else{command.CommandText="UPDATE dbo.witnesses SET name=@name,side=@side,role=@role,contact_info=@contact,subpoena_status=@subpoena,outline_notes=@outline,notes=@notes,updated_at=@now OUTPUT INSERTED.id,INSERTED.row_version WHERE id=@id AND row_version=@version AND is_deleted=0";command.Parameters.Add(new SqlParameter("@id",model.Id));command.Parameters.Add(new SqlParameter("@version",ExpectedVersion(model.RowVersion,"witness",model.Id)));}
        command.Parameters.Add(new SqlParameter("@caseId",model.CaseId));command.Parameters.Add(new SqlParameter("@name",model.Name??""));command.Parameters.Add(new SqlParameter("@side",model.Side??"ASHC"));command.Parameters.Add(new SqlParameter("@role",Db(model.Role)));command.Parameters.Add(new SqlParameter("@contact",Db(model.ContactInfo)));command.Parameters.Add(new SqlParameter("@subpoena",model.SubpoenaStatus??"Not Needed"));command.Parameters.Add(new SqlParameter("@outline",Db(model.OutlineNotes)));command.Parameters.Add(new SqlParameter("@notes",Db(model.Notes)));command.Parameters.Add(new SqlParameter("@now",now));
        await using(var reader=await command.ExecuteReaderAsync(token)){if(!await reader.ReadAsync(token))throw new WorkItemConcurrencyException("Witness",model.Id);model.Id=reader.GetInt64(0);model.RowVersion=Convert.ToBase64String((byte[])reader.GetValue(1));}
        await AuditAsync(connection,transaction,model.CaseId,isNew?"WitnessCreated":"WitnessUpdated","Witness",model.Id,token);await transaction.CommitAsync(token);return model;
    }
    public Task DeleteAsync(long id,string? rowVersion=null,CancellationToken token=default)=>SoftDeleteAsync("witnesses","Witness","Witness",id,rowVersion,token);
}

public sealed class SqlServerExhibitStore(IDatabaseConnectionFactory connections,IHttpContextAccessor accessor)
    : SqlServerLitigationStoreBase(connections,accessor),IExhibitStore
{
    public string Provider=>"SqlServer";
    public async Task<List<ExhibitRecord>> GetAsync(long? caseId,CancellationToken token=default)
    {
        var result=new List<ExhibitRecord>();await using var connection=Connections.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();command.CommandText="SELECT id,case_id,label,side,description,status,notes,row_version FROM dbo.exhibits WHERE is_deleted=0 AND (@caseId IS NULL OR case_id=@caseId) ORDER BY side,label";command.Parameters.Add(new SqlParameter("@caseId",(object?)caseId??DBNull.Value));await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))result.Add(new(){Id=reader.GetInt64(0),CaseId=reader.GetInt64(1),Label=Text(reader,2)??"",Side=Text(reader,3)??"ASHC",Description=Text(reader,4),Status=Text(reader,5)??"Pre-Labeled",Notes=Text(reader,6),RowVersion=Convert.ToBase64String((byte[])reader.GetValue(7))});return result;
    }
    public async Task<ExhibitRecord> SaveAsync(ExhibitRecord model,CancellationToken token=default)
    {
        var isNew=model.Id==0;await using var connection=Connections.CreateConnection();await connection.OpenAsync(token);await using var transaction=await connection.BeginTransactionAsync(token);model.CaseId=await ResolveCaseIdAsync(connection,transaction,"exhibits",model.Id,model.CaseId,token);var now=DateTime.UtcNow.ToString("O");await using var command=connection.CreateCommand();command.Transaction=transaction;
        if(isNew)command.CommandText="INSERT INTO dbo.exhibits (case_id,label,side,description,status,notes,created_at,updated_at) OUTPUT INSERTED.id,INSERTED.row_version VALUES (@caseId,@label,@side,@description,@status,@notes,@now,@now)";
        else{command.CommandText="UPDATE dbo.exhibits SET label=@label,side=@side,description=@description,status=@status,notes=@notes,updated_at=@now OUTPUT INSERTED.id,INSERTED.row_version WHERE id=@id AND row_version=@version AND is_deleted=0";command.Parameters.Add(new SqlParameter("@id",model.Id));command.Parameters.Add(new SqlParameter("@version",ExpectedVersion(model.RowVersion,"exhibit",model.Id)));}
        command.Parameters.Add(new SqlParameter("@caseId",model.CaseId));command.Parameters.Add(new SqlParameter("@label",model.Label??""));command.Parameters.Add(new SqlParameter("@side",model.Side??"ASHC"));command.Parameters.Add(new SqlParameter("@description",Db(model.Description)));command.Parameters.Add(new SqlParameter("@status",model.Status??"Pre-Labeled"));command.Parameters.Add(new SqlParameter("@notes",Db(model.Notes)));command.Parameters.Add(new SqlParameter("@now",now));
        await using(var reader=await command.ExecuteReaderAsync(token)){if(!await reader.ReadAsync(token))throw new WorkItemConcurrencyException("Exhibit",model.Id);model.Id=reader.GetInt64(0);model.RowVersion=Convert.ToBase64String((byte[])reader.GetValue(1));}await AuditAsync(connection,transaction,model.CaseId,isNew?"ExhibitCreated":"ExhibitUpdated","Exhibit",model.Id,token);await transaction.CommitAsync(token);return model;
    }
    public Task DeleteAsync(long id,string? rowVersion=null,CancellationToken token=default)=>SoftDeleteAsync("exhibits","Exhibit","Exhibit",id,rowVersion,token);
}

public sealed class SqlServerTrialMotionStore(IDatabaseConnectionFactory connections,IHttpContextAccessor accessor)
    : SqlServerLitigationStoreBase(connections,accessor),ITrialMotionStore
{
    public string Provider=>"SqlServer";
    public async Task<List<TrialMotionRecord>> GetAsync(long? caseId,CancellationToken token=default)
    {
        var result=new List<TrialMotionRecord>();await using var connection=Connections.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();command.CommandText="SELECT id,case_id,title,filed_by,filed_date,status,notes,row_version FROM dbo.trial_motions WHERE is_deleted=0 AND (@caseId IS NULL OR case_id=@caseId) ORDER BY COALESCE(filed_date,'9999-12-31')";command.Parameters.Add(new SqlParameter("@caseId",(object?)caseId??DBNull.Value));await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))result.Add(new(){Id=reader.GetInt64(0),CaseId=reader.GetInt64(1),Title=Text(reader,2)??"",FiledBy=Text(reader,3)??"ASHC",FiledDate=Date(Text(reader,4)),Status=Text(reader,5)??"Pending",Notes=Text(reader,6),RowVersion=Convert.ToBase64String((byte[])reader.GetValue(7))});return result;
    }
    public async Task<TrialMotionRecord> SaveAsync(TrialMotionRecord model,CancellationToken token=default)
    {
        var isNew=model.Id==0;await using var connection=Connections.CreateConnection();await connection.OpenAsync(token);await using var transaction=await connection.BeginTransactionAsync(token);model.CaseId=await ResolveCaseIdAsync(connection,transaction,"trial_motions",model.Id,model.CaseId,token);var now=DateTime.UtcNow.ToString("O");await using var command=connection.CreateCommand();command.Transaction=transaction;
        if(isNew)command.CommandText="INSERT INTO dbo.trial_motions (case_id,title,filed_by,filed_date,status,notes,created_at,updated_at) OUTPUT INSERTED.id,INSERTED.row_version VALUES (@caseId,@title,@filedBy,@filedDate,@status,@notes,@now,@now)";
        else{command.CommandText="UPDATE dbo.trial_motions SET title=@title,filed_by=@filedBy,filed_date=@filedDate,status=@status,notes=@notes,updated_at=@now OUTPUT INSERTED.id,INSERTED.row_version WHERE id=@id AND row_version=@version AND is_deleted=0";command.Parameters.Add(new SqlParameter("@id",model.Id));command.Parameters.Add(new SqlParameter("@version",ExpectedVersion(model.RowVersion,"trial motion",model.Id)));}
        command.Parameters.Add(new SqlParameter("@caseId",model.CaseId));command.Parameters.Add(new SqlParameter("@title",model.Title??""));command.Parameters.Add(new SqlParameter("@filedBy",model.FiledBy??"ASHC"));command.Parameters.Add(new SqlParameter("@filedDate",Db(Date(model.FiledDate))));command.Parameters.Add(new SqlParameter("@status",model.Status??"Pending"));command.Parameters.Add(new SqlParameter("@notes",Db(model.Notes)));command.Parameters.Add(new SqlParameter("@now",now));
        await using(var reader=await command.ExecuteReaderAsync(token)){if(!await reader.ReadAsync(token))throw new WorkItemConcurrencyException("Trial motion",model.Id);model.Id=reader.GetInt64(0);model.RowVersion=Convert.ToBase64String((byte[])reader.GetValue(1));}await AuditAsync(connection,transaction,model.CaseId,isNew?"TrialMotionCreated":"TrialMotionUpdated","TrialMotion",model.Id,token);await transaction.CommitAsync(token);return model;
    }
    public Task DeleteAsync(long id,string? rowVersion=null,CancellationToken token=default)=>SoftDeleteAsync("trial_motions","Trial motion","TrialMotion",id,rowVersion,token);
}
