using System.Data.Common;
using CasePlanner.Data;
using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Security;
using CasePlanner.Web.Server.Services;
using Microsoft.Data.SqlClient;

namespace CasePlanner.Web.Server.Persistence;

public interface IIssueTagStore
{
    Task<List<IssueTagRecord>> GetCatalogAsync(CancellationToken token = default);
    Task<IssueTagRecord> CreateAsync(string name, string? description, string? category, CancellationToken token = default);
    Task<IssueTagRecord> RenameAsync(long id, string name, string? description, string? category, CancellationToken token = default);
    Task RetireAsync(long id, CancellationToken token = default);
    Task<List<IssueTagUsage>> GetUsageAsync(CancellationToken token = default);
    Task<List<CaseIssueTagRecord>> GetCaseTagsAsync(long caseId, CancellationToken token = default);
    Task<CaseIssueTagRecord> AddAsync(long caseId, long issueTagId, CancellationToken token = default);
    Task<long?> GetCaseIdAsync(long assignmentId, CancellationToken token = default);
    Task RemoveAsync(long assignmentId, string? rowVersion = null, CancellationToken token = default);
}

public sealed class SqliteIssueTagStore(CasePlannerRepository repository) : IIssueTagStore
{
    public Task<List<IssueTagRecord>> GetCatalogAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.GetIssueTagsAsync();
    }

    public Task<IssueTagRecord> CreateAsync(string name, string? description, string? category, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.CreateIssueTagAsync(name, description, category);
    }

    public Task<IssueTagRecord> RenameAsync(long id, string name, string? description, string? category, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.RenameIssueTagAsync(id, name, description, category);
    }

    public Task RetireAsync(long id, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.RetireIssueTagAsync(id);
    }

    public Task<List<IssueTagUsage>> GetUsageAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.GetIssueTagUsageAsync();
    }

    public Task<List<CaseIssueTagRecord>> GetCaseTagsAsync(long caseId, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.GetCaseIssueTagsAsync(caseId);
    }

    public async Task<CaseIssueTagRecord> AddAsync(long caseId, long issueTagId, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        await repository.AddIssueTagAsync(caseId, issueTagId);
        return (await repository.GetCaseIssueTagsAsync(caseId)).Single(x => x.IssueTagId == issueTagId);
    }

    public Task<long?> GetCaseIdAsync(long assignmentId, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.GetChildCaseIdAsync("case-issue-tag", assignmentId);
    }

    public Task RemoveAsync(long assignmentId, string? rowVersion = null, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.RemoveIssueTagAsync(assignmentId);
    }
}

public sealed class SqlServerIssueTagStore(
    IDatabaseConnectionFactory connections,
    IApplicationActorContext actor) : IIssueTagStore
{
    public async Task<List<IssueTagRecord>> GetCatalogAsync(CancellationToken token = default)
    {
        var result = new List<IssueTagRecord>();
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,name,description,category FROM dbo.issue_tags WHERE is_deleted=0 ORDER BY category,name";
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            result.Add(new() { Id=reader.GetInt64(0),Name=Text(reader,1)??"",Description=Text(reader,2),Category=Text(reader,3) });
        return result;
    }

    public async Task<IssueTagRecord> CreateAsync(string name,string? description,string? category,CancellationToken token=default)
    {
        if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("A tag name is required.",nameof(name));
        await using var connection=connections.CreateConnection();await connection.OpenAsync(token);
        await using var command=connection.CreateCommand();
        command.CommandText="""
            INSERT INTO dbo.issue_tags(name,description,category) OUTPUT INSERTED.id VALUES(@name,@description,@category)
            """;
        command.Parameters.Add(new SqlParameter("@name",name));
        command.Parameters.Add(new SqlParameter("@description",(object?)description??DBNull.Value));
        command.Parameters.Add(new SqlParameter("@category",(object?)category??DBNull.Value));
        long id;
        try{id=Convert.ToInt64(await command.ExecuteScalarAsync(token));}
        catch(SqlException ex) when(ex.Number is 2601 or 2627){throw new DuplicateIssueTagException($"An issue tag named '{name}' already exists.");}
        return new IssueTagRecord{Id=id,Name=name,Description=description,Category=category};
    }

    public async Task<IssueTagRecord> RenameAsync(long id,string name,string? description,string? category,CancellationToken token=default)
    {
        if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("A tag name is required.",nameof(name));
        await using var connection=connections.CreateConnection();await connection.OpenAsync(token);
        await using var command=connection.CreateCommand();
        command.CommandText="UPDATE dbo.issue_tags SET name=@name,description=@description,category=@category WHERE id=@id AND is_deleted=0";
        command.Parameters.Add(new SqlParameter("@name",name));
        command.Parameters.Add(new SqlParameter("@description",(object?)description??DBNull.Value));
        command.Parameters.Add(new SqlParameter("@category",(object?)category??DBNull.Value));
        command.Parameters.Add(new SqlParameter("@id",id));
        int affected;
        try{affected=await command.ExecuteNonQueryAsync(token);}
        catch(SqlException ex) when(ex.Number is 2601 or 2627){throw new DuplicateIssueTagException($"An issue tag named '{name}' already exists.");}
        if(affected==0)throw new InvalidOperationException("Issue tag not found.");
        return new IssueTagRecord{Id=id,Name=name,Description=description,Category=category};
    }

    public async Task RetireAsync(long id,CancellationToken token=default)
    {
        await using var connection=connections.CreateConnection();await connection.OpenAsync(token);
        await using var command=connection.CreateCommand();
        command.CommandText="UPDATE dbo.issue_tags SET is_deleted=1 WHERE id=@id";
        command.Parameters.Add(new SqlParameter("@id",id));
        if(await command.ExecuteNonQueryAsync(token)==0)throw new InvalidOperationException("Issue tag not found.");
    }

    public async Task<List<IssueTagUsage>> GetUsageAsync(CancellationToken token=default)
    {
        await using var connection=connections.CreateConnection();await connection.OpenAsync(token);
        await using var command=connection.CreateCommand();
        command.CommandText="""
            SELECT dts.issue_tag_name,dt.title
            FROM dbo.document_template_sections dts
            JOIN dbo.document_template_versions dtv ON dtv.id=dts.template_version_id
            JOIN dbo.document_templates dt ON dt.id=dtv.template_id
            WHERE dts.issue_tag_name IS NOT NULL AND dt.is_deleted=0
            """;
        var byTag=new Dictionary<string,List<string>>(StringComparer.OrdinalIgnoreCase);
        await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token))
        {
            var tag=reader.GetString(0);var title=reader.GetString(1);
            if(!byTag.TryGetValue(tag,out var list))byTag[tag]=list=[];
            if(!list.Contains(title))list.Add(title);
        }
        return byTag.Select(kv=>new IssueTagUsage{TagName=kv.Key,TemplateTitles=kv.Value}).ToList();
    }

    public async Task<List<CaseIssueTagRecord>> GetCaseTagsAsync(long caseId, CancellationToken token = default)
    {
        var result = new List<CaseIssueTagRecord>();
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT cit.id,cit.case_id,cit.issue_tag_id,it.name,it.category,it.description,cit.notes,cit.row_version
            FROM dbo.case_issue_tags cit
            JOIN dbo.issue_tags it ON it.id=cit.issue_tag_id
            WHERE cit.case_id=@caseId AND cit.is_deleted=0
            ORDER BY it.category,it.name
            """;
        command.Parameters.Add(new SqlParameter("@caseId",caseId));
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            result.Add(new() { Id=reader.GetInt64(0),CaseId=reader.GetInt64(1),IssueTagId=reader.GetInt64(2),TagName=Text(reader,3)??"",Category=Text(reader,4),Description=Text(reader,5),Notes=Text(reader,6),RowVersion=Convert.ToBase64String((byte[])reader.GetValue(7)) });
        return result;
    }

    public async Task<CaseIssueTagRecord> AddAsync(long caseId,long issueTagId,CancellationToken token=default)
    {
        await using var connection=connections.CreateConnection();await connection.OpenAsync(token);await using var transaction=await connection.BeginTransactionAsync(token);
        var now=DateTime.UtcNow.ToString("O");long id;
        await using(var command=connection.CreateCommand())
        {
            command.Transaction=transaction;command.CommandText="""
                INSERT INTO dbo.case_issue_tags(case_id,issue_tag_id,notes,created_at,updated_at,created_by_user_id,is_deleted)
                OUTPUT INSERTED.id VALUES(@case,@tag,NULL,@now,@now,@actor,0)
                """;
            command.Parameters.Add(new SqlParameter("@case",caseId));command.Parameters.Add(new SqlParameter("@tag",issueTagId));command.Parameters.Add(new SqlParameter("@now",now));command.Parameters.Add(new SqlParameter("@actor",(object?)actor.UserId??DBNull.Value));
            try{id=Convert.ToInt64(await command.ExecuteScalarAsync(token));}
            catch(SqlException ex) when(ex.Number is 2601 or 2627){throw new DuplicateIssueTagException("This issue tag is already assigned to the case.");}
            catch(SqlException ex) when(ex.Number==547){throw new InvalidOperationException("The case or issue tag does not exist in SQL Server.");}
        }
        await GenerateChecklistAsync(connection,transaction,caseId,token);
        await AuditAsync(connection,transaction,caseId,"CaseIssueTagAdded",id,token);
        await transaction.CommitAsync(token);
        return (await GetCaseTagsAsync(caseId,token)).Single(x=>x.Id==id);
    }

    public async Task<long?> GetCaseIdAsync(long assignmentId,CancellationToken token=default)
    {
        await using var connection=connections.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();
        command.CommandText="SELECT case_id FROM dbo.case_issue_tags WHERE id=@id AND is_deleted=0";command.Parameters.Add(new SqlParameter("@id",assignmentId));
        return await command.ExecuteScalarAsync(token) is { } value?Convert.ToInt64(value):null;
    }

    public async Task RemoveAsync(long assignmentId,string? rowVersion=null,CancellationToken token=default)
    {
        var version=ExpectedVersion(rowVersion,"case issue tag",assignmentId);
        await using var connection=connections.CreateConnection();await connection.OpenAsync(token);await using var transaction=await connection.BeginTransactionAsync(token);
        long caseId;string tagName;
        await using(var command=connection.CreateCommand())
        {
            command.Transaction=transaction;command.CommandText="""
                SELECT cit.case_id,it.name FROM dbo.case_issue_tags cit JOIN dbo.issue_tags it ON it.id=cit.issue_tag_id
                WHERE cit.id=@id AND cit.is_deleted=0
                """;command.Parameters.Add(new SqlParameter("@id",assignmentId));
            await using var reader=await command.ExecuteReaderAsync(token);if(!await reader.ReadAsync(token))throw new InvalidOperationException("Case issue tag not found.");
            caseId=reader.GetInt64(0);tagName=reader.GetString(1);
        }
        await using(var command=connection.CreateCommand())
        {
            command.Transaction=transaction;command.CommandText="""
                UPDATE dbo.case_issue_tags SET is_deleted=1,deleted_utc=SYSUTCDATETIME(),deleted_by_user_id=@actor
                WHERE id=@id AND row_version=@version AND is_deleted=0
                """;command.Parameters.Add(new SqlParameter("@actor",(object?)actor.UserId??DBNull.Value));command.Parameters.Add(new SqlParameter("@id",assignmentId));command.Parameters.Add(new SqlParameter("@version",version));
            if(await command.ExecuteNonQueryAsync(token)==0)throw new WorkItemConcurrencyException("Case issue tag",assignmentId);
        }
        await using(var command=connection.CreateCommand())
        {
            command.Transaction=transaction;command.CommandText="""
                UPDATE ci SET status='N/A',
                    notes=CASE WHEN COALESCE(ci.notes,'')='' THEN @why ELSE CONCAT(ci.notes,' | ',@why) END,
                    updated_at=@now
                FROM dbo.checklist_items ci
                WHERE ci.case_id=@case AND ci.is_deleted=0 AND ci.is_manual=0
                  AND ci.status NOT IN('Done','Complete','N/A')
                  AND ci.source_type IN(
                    SELECT CONCAT('Template:',t.name,':',ti.sort_order)
                    FROM dbo.checklist_template_items ti JOIN dbo.checklist_templates t ON t.id=ti.template_id
                    WHERE ti.is_deleted=0 AND t.is_deleted=0 AND t.trigger_type='IssueTag' AND t.issue_tag_name=@tag)
                """;
            command.Parameters.Add(new SqlParameter("@why",$"Auto-marked N/A: issue tag '{tagName}' removed."));command.Parameters.Add(new SqlParameter("@now",DateTime.UtcNow.ToString("O")));command.Parameters.Add(new SqlParameter("@case",caseId));command.Parameters.Add(new SqlParameter("@tag",tagName));await command.ExecuteNonQueryAsync(token);
        }
        await AuditAsync(connection,transaction,caseId,"CaseIssueTagRemoved",assignmentId,token);await transaction.CommitAsync(token);
    }

    private async Task GenerateChecklistAsync(DbConnection connection,DbTransaction transaction,long caseId,CancellationToken token)
    {
        await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="""
            INSERT INTO dbo.checklist_items(case_id,phase,task,due_date,status,notes,source_type,is_manual,created_at,updated_at,source_kind,source_template_id,source_template_version,source_stage,generated_at,generated_by)
            SELECT c.id,ti.phase,ti.task,CASE WHEN ti.due_offset_days IS NULL THEN NULL ELSE CONVERT(nvarchar(10),DATEADD(day,ti.due_offset_days,CAST(GETDATE() AS date)),23) END,
                   'Not Started',NULL,CONCAT('Template:',t.name,':',ti.sort_order),0,@now,@now,'StageTemplate',CONCAT(t.name,':',ti.sort_order),1,
                   COALESCE(NULLIF(c.case_status,''),c.status),@now,@display
            FROM dbo.cases c
            JOIN dbo.case_issue_tags cit ON cit.case_id=c.id AND cit.is_deleted=0
            JOIN dbo.issue_tags it ON it.id=cit.issue_tag_id
            JOIN dbo.checklist_templates t ON t.trigger_type='IssueTag' AND t.issue_tag_name=it.name AND t.active=1 AND t.is_deleted=0
            JOIN dbo.checklist_template_items ti ON ti.template_id=t.id AND ti.is_deleted=0
            WHERE c.id=@case AND c.is_deleted=0 AND COALESCE(NULLIF(c.case_status,''),c.status) NOT IN('Triage','Pipeline')
              AND (t.track='Any' OR t.track=c.track)
              AND NOT EXISTS(SELECT 1 FROM dbo.checklist_items x WHERE x.case_id=c.id AND x.is_deleted=0 AND
                    (x.source_type=CONCAT('Template:',t.name,':',ti.sort_order) OR (COALESCE(x.phase,'')=COALESCE(ti.phase,'') AND x.task=ti.task)))
            """;
        command.Parameters.Add(new SqlParameter("@now",DateTime.UtcNow.ToString("O")));command.Parameters.Add(new SqlParameter("@display",actor.AuditLabel));command.Parameters.Add(new SqlParameter("@case",caseId));await command.ExecuteNonQueryAsync(token);
    }

    private async Task AuditAsync(DbConnection connection,DbTransaction transaction,long caseId,string action,long id,CancellationToken token)
    {
        await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="INSERT INTO dbo.audit_events(case_id,actor_user_id,action,entity_type,entity_id) VALUES(@case,@actor,@action,'CaseIssueTag',@id)";
        command.Parameters.Add(new SqlParameter("@case",caseId));command.Parameters.Add(new SqlParameter("@actor",(object?)actor.UserId??DBNull.Value));command.Parameters.Add(new SqlParameter("@action",action));command.Parameters.Add(new SqlParameter("@id",id.ToString()));await command.ExecuteNonQueryAsync(token);
    }
    private static string? Text(DbDataReader reader,int i)=>reader.IsDBNull(i)?null:Convert.ToString(reader.GetValue(i));
    private static byte[] ExpectedVersion(string? value,string kind,long id){if(string.IsNullOrWhiteSpace(value))throw new ArgumentException($"RowVersion is required when changing SQL Server {kind} {id}.");try{return Convert.FromBase64String(value);}catch(FormatException){throw new ArgumentException($"The {kind} RowVersion is not valid.");}}
}
