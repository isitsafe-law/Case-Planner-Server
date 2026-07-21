using System.Data.Common;
using CasePlanner.Data;
using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;
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
        command.CommandText="SELECT id,case_id,name,side,role,contact_info,subpoena_status,outline_notes,notes,row_version,person_id FROM dbo.witnesses WHERE is_deleted=0 AND (@caseId IS NULL OR case_id=@caseId) ORDER BY side,name";
        command.Parameters.Add(new SqlParameter("@caseId",(object?)caseId??DBNull.Value));await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token))result.Add(new(){Id=reader.GetInt64(0),CaseId=reader.GetInt64(1),Name=Text(reader,2)??"",Side=Text(reader,3)??"ASHC",Role=Text(reader,4),ContactInfo=Text(reader,5),SubpoenaStatus=Text(reader,6)??"Not Needed",OutlineNotes=Text(reader,7),Notes=Text(reader,8),RowVersion=Convert.ToBase64String((byte[])reader.GetValue(9)),PersonId=reader.IsDBNull(10)?null:reader.GetInt64(10)});return result;
    }
    public async Task<WitnessRecord> SaveAsync(WitnessRecord model,CancellationToken token=default)
    {
        var isNew=model.Id==0;await using var connection=Connections.CreateConnection();await connection.OpenAsync(token);await using var transaction=await connection.BeginTransactionAsync(token);model.CaseId=await ResolveCaseIdAsync(connection,transaction,"witnesses",model.Id,model.CaseId,token);var now=DateTime.UtcNow.ToString("O");
        // Multi-user rollout Phase 3: same link-or-create rule as the SQLite side (CasePlannerRepository.
        // ResolveOrCreateWitnessPersonAsync) - client-resolved person wins, else an EXACT normalized-name
        // match to an existing witness_persons row, else create a new one. Only on insert; an existing
        // witness's link is left alone unless the client explicitly sends a different PersonId.
        if(isNew)model.PersonId=await ResolveOrCreateWitnessPersonAsync(connection,transaction,model.PersonId,model.Name,model.ContactInfo,now,token);
        await using var command=connection.CreateCommand();command.Transaction=transaction;
        if(isNew)command.CommandText="INSERT INTO dbo.witnesses (case_id,name,side,role,contact_info,subpoena_status,outline_notes,notes,person_id,created_at,updated_at) OUTPUT INSERTED.id,INSERTED.row_version,INSERTED.person_id VALUES (@caseId,@name,@side,@role,@contact,@subpoena,@outline,@notes,@personId,@now,@now)";
        else{command.CommandText="UPDATE dbo.witnesses SET name=@name,side=@side,role=@role,contact_info=@contact,subpoena_status=@subpoena,outline_notes=@outline,notes=@notes,person_id=COALESCE(@personId,person_id),updated_at=@now OUTPUT INSERTED.id,INSERTED.row_version,INSERTED.person_id WHERE id=@id AND row_version=@version AND is_deleted=0";command.Parameters.Add(new SqlParameter("@id",model.Id));command.Parameters.Add(new SqlParameter("@version",ExpectedVersion(model.RowVersion,"witness",model.Id)));}
        command.Parameters.Add(new SqlParameter("@caseId",model.CaseId));command.Parameters.Add(new SqlParameter("@name",model.Name??""));command.Parameters.Add(new SqlParameter("@side",model.Side??"ASHC"));command.Parameters.Add(new SqlParameter("@role",Db(model.Role)));command.Parameters.Add(new SqlParameter("@contact",Db(model.ContactInfo)));command.Parameters.Add(new SqlParameter("@subpoena",model.SubpoenaStatus??"Not Needed"));command.Parameters.Add(new SqlParameter("@outline",Db(model.OutlineNotes)));command.Parameters.Add(new SqlParameter("@notes",Db(model.Notes)));command.Parameters.Add(new SqlParameter("@personId",(object?)model.PersonId??DBNull.Value));command.Parameters.Add(new SqlParameter("@now",now));
        await using(var reader=await command.ExecuteReaderAsync(token)){if(!await reader.ReadAsync(token))throw new WorkItemConcurrencyException("Witness",model.Id);model.Id=reader.GetInt64(0);model.RowVersion=Convert.ToBase64String((byte[])reader.GetValue(1));model.PersonId=reader.IsDBNull(2)?null:reader.GetInt64(2);}
        await AuditAsync(connection,transaction,model.CaseId,isNew?"WitnessCreated":"WitnessUpdated","Witness",model.Id,token);await transaction.CommitAsync(token);return model;
    }
    public Task DeleteAsync(long id,string? rowVersion=null,CancellationToken token=default)=>SoftDeleteAsync("witnesses","Witness","Witness",id,rowVersion,token);

    private static async Task<long> ResolveOrCreateWitnessPersonAsync(DbConnection connection,DbTransaction transaction,long? explicitPersonId,string name,string? contactInfo,string now,CancellationToken token)
    {
        if(explicitPersonId is > 0)return explicitPersonId.Value;
        var normalized=WitnessNameMatcher.Normalize(name);
        if(normalized.Length>0)
        {
            await using var findCommand=connection.CreateCommand();findCommand.Transaction=transaction;
            findCommand.CommandText="SELECT TOP 1 id FROM dbo.witness_persons WHERE is_deleted=0 AND LOWER(LTRIM(RTRIM(name)))=@normalized";
            findCommand.Parameters.Add(new SqlParameter("@normalized",normalized));
            var existing=await findCommand.ExecuteScalarAsync(token);
            if(existing is not null)return Convert.ToInt64(existing);
        }
        await using var insertCommand=connection.CreateCommand();insertCommand.Transaction=transaction;
        insertCommand.CommandText="INSERT INTO dbo.witness_persons (name,contact_info,created_at,updated_at) OUTPUT INSERTED.id VALUES (@name,@contact,@now,@now)";
        insertCommand.Parameters.Add(new SqlParameter("@name",name??""));insertCommand.Parameters.Add(new SqlParameter("@contact",Db(contactInfo)));insertCommand.Parameters.Add(new SqlParameter("@now",now));
        return Convert.ToInt64(await insertCommand.ExecuteScalarAsync(token));
    }
}

// Multi-user rollout Phase 3: SQL Server side of the witness registry search/autofill endpoint.
// Mirrors CasePlannerRepository.SearchWitnessPersonsAsync's ranking exactly (same shared
// WitnessNameMatcher, same rank order, same 10/200 caps) so the client's suggestion/flag behavior
// is identical regardless of which provider is active. No live SQL Server sandbox available here
// to exercise this against a real pilot instance.
public sealed class SqlServerWitnessRegistryStore(IDatabaseConnectionFactory connections,IHttpContextAccessor accessor)
    : SqlServerLitigationStoreBase(connections,accessor),IWitnessRegistryStore
{
    public string Provider=>"SqlServer";
    public async Task<List<WitnessPersonMatch>> SearchAsync(string? query,CancellationToken token=default)
    {
        var people=new List<(long Id,string Name,string? ContactInfo)>();
        await using var connection=Connections.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();
        command.CommandText="SELECT id,name,contact_info FROM dbo.witness_persons WHERE is_deleted=0 ORDER BY name";
        await using(var reader=await command.ExecuteReaderAsync(token))
            while(await reader.ReadAsync(token))people.Add((reader.GetInt64(0),Text(reader,1)??"",Text(reader,2)));

        var normalizedQuery=WitnessNameMatcher.Normalize(query);
        var ranked=new List<(long Id,string Name,string? ContactInfo,string MatchType,int Rank)>();
        if(normalizedQuery.Length==0)
        {
            ranked.AddRange(people.Select(p=>(p.Id,p.Name,p.ContactInfo,"exact",0)));
        }
        else
        {
            foreach(var person in people)
            {
                var normalizedName=WitnessNameMatcher.Normalize(person.Name);
                if(normalizedName==normalizedQuery)ranked.Add((person.Id,person.Name,person.ContactInfo,"exact",0));
                else if(normalizedName.StartsWith(normalizedQuery,StringComparison.Ordinal))ranked.Add((person.Id,person.Name,person.ContactInfo,"exact",1));
                else if(normalizedName.Contains(normalizedQuery,StringComparison.Ordinal))ranked.Add((person.Id,person.Name,person.ContactInfo,"exact",2));
                else if(WitnessNameMatcher.AreSimilar(normalizedQuery,normalizedName))ranked.Add((person.Id,person.Name,person.ContactInfo,"similar",3));
            }
        }

        var capped=ranked.OrderBy(r=>r.Rank).ThenBy(r=>r.Name).Take(normalizedQuery.Length==0?200:10).ToList();
        if(capped.Count==0)return [];

        var caseNamesByPerson=new Dictionary<long,List<string>>();
        // IDs come from the witness_persons rows just read above (system-generated bigints, never
        // user input), so inlining them is safe and avoids a STRING_SPLIT compatibility-level
        // dependency - same approach as the SQLite side's equivalent query.
        var personIdList=string.Join(",",capped.Select(c=>c.Id));
        await using(var caseCommand=connection.CreateCommand())
        {
            caseCommand.CommandText=$"""
                SELECT w.person_id, c.case_number
                FROM dbo.witnesses w
                JOIN dbo.cases c ON c.id = w.case_id
                WHERE w.is_deleted=0 AND w.person_id IN ({personIdList})
                """;
            await using var caseReader=await caseCommand.ExecuteReaderAsync(token);
            while(await caseReader.ReadAsync(token))
            {
                var personId=caseReader.GetInt64(0);
                var caseNumber=Text(caseReader,1);
                if(string.IsNullOrWhiteSpace(caseNumber))continue;
                if(!caseNamesByPerson.TryGetValue(personId,out var list)){list=[];caseNamesByPerson[personId]=list;}
                if(!list.Contains(caseNumber))list.Add(caseNumber);
            }
        }

        return capped.Select(r=>new WitnessPersonMatch{Id=r.Id,Name=r.Name,ContactInfo=r.ContactInfo,MatchType=r.MatchType,OtherCaseNumbers=caseNamesByPerson.TryGetValue(r.Id,out var cases)?cases:[]}).ToList();
    }

    // Multi-user rollout Phase 4 (witness cross-reference lookup): mirrors CasePlannerRepository.
    // GetWitnessPersonDetailAsync exactly (same "open case" definition - excludes both the legacy
    // status column and the consolidated case_status 'Resolved / Closed' bucket - and same
    // Deposition-type hearings join for event dates). No live SQL Server sandbox available here to
    // exercise this against a real pilot instance - same caveat as the rest of this dormant
    // multi-user foundation.
    public async Task<WitnessPersonDetail?> GetPersonDetailAsync(long personId,CancellationToken token=default)
    {
        await using var connection=Connections.CreateConnection();await connection.OpenAsync(token);

        string name;string? contactInfo;
        await using(var command=connection.CreateCommand())
        {
            command.CommandText="SELECT name,contact_info FROM dbo.witness_persons WHERE id=@id AND is_deleted=0";
            command.Parameters.Add(new SqlParameter("@id",personId));
            await using var reader=await command.ExecuteReaderAsync(token);
            if(!await reader.ReadAsync(token))return null;
            name=Text(reader,0)??"";contactInfo=Text(reader,1);
        }

        var cases=new List<WitnessPersonCase>();
        await using(var command=connection.CreateCommand())
        {
            command.CommandText="""
                SELECT DISTINCT c.id, c.case_number, c.case_name, c.trial_date, c.trial_end_date
                FROM dbo.witnesses w
                JOIN dbo.cases c ON c.id = w.case_id
                WHERE w.is_deleted=0 AND w.person_id=@personId AND COALESCE(c.is_deleted,0)=0
                  AND COALESCE(c.case_status,N'Pipeline') <> N'Resolved / Closed'
                  AND COALESCE(c.status,N'') NOT IN (N'Closed', N'Complete')
                ORDER BY c.case_name
                """;
            command.Parameters.Add(new SqlParameter("@personId",personId));
            await using var reader=await command.ExecuteReaderAsync(token);
            while(await reader.ReadAsync(token))
            {
                cases.Add(new WitnessPersonCase{CaseId=reader.GetInt64(0),CaseNumber=Text(reader,1)??"",CaseName=Text(reader,2)??"",TrialDate=Text(reader,3),TrialEndDate=Text(reader,4)});
            }
        }

        if(cases.Count>0)
        {
            var caseIdList=string.Join(",",cases.Select(c=>c.CaseId));
            var depositionsByCase=new Dictionary<long,List<WitnessDepositionEvent>>();
            await using(var command=connection.CreateCommand())
            {
                command.CommandText=$"""
                    SELECT case_id, title, hearing_date
                    FROM dbo.hearings
                    WHERE is_deleted=0 AND event_type = N'Deposition' AND case_id IN ({caseIdList})
                    ORDER BY COALESCE(hearing_date, N'9999-12-31')
                    """;
                await using var reader=await command.ExecuteReaderAsync(token);
                while(await reader.ReadAsync(token))
                {
                    var caseId=reader.GetInt64(0);
                    if(!depositionsByCase.TryGetValue(caseId,out var list)){list=[];depositionsByCase[caseId]=list;}
                    list.Add(new WitnessDepositionEvent{Title=Text(reader,1)??"",Date=Text(reader,2)});
                }
            }
            foreach(var c in cases)if(depositionsByCase.TryGetValue(c.CaseId,out var events))c.DepositionEvents=events;
        }

        return new WitnessPersonDetail{Id=personId,Name=name,ContactInfo=contactInfo,Cases=cases};
    }
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
