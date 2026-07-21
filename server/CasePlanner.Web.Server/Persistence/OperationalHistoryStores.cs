using System.Data.Common;
using CasePlanner.Data;
using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Security;
using CasePlanner.Web.Server.Services;
using Microsoft.Data.SqlClient;

namespace CasePlanner.Web.Server.Persistence;

public interface IDiscoveryPostureStore
{
    string Provider { get; }
    Task<DiscoveryPosture?> GetAsync(long caseId, CancellationToken token = default);
    Task<DiscoveryPosture> SaveAsync(DiscoveryPosture model, CancellationToken token = default);
}

public interface IPipelineHandoffStore
{
    string Provider { get; }
    // caseId is nullable so a single store method backs both the per-case handoff-history dialog
    // (a concrete caseId) and the cross-case Report C (Cycle-Time) bulk fetch (null = every case) -
    // same convention as IDeadlineStore.GetAsync above.
    Task<List<PipelineHandoffRecord>> GetAsync(long? caseId, CancellationToken token = default);
    Task<PipelineHandoffRecord> SaveAsync(long caseId, PipelineHandoffRequest request, CancellationToken token = default);
}

public interface IActivityStore
{
    string Provider { get; }
    Task<List<ActivityLogEntry>> GetAsync(long? caseId, CancellationToken token = default);
    Task<long?> GetCaseIdAsync(long activityId, CancellationToken token = default);
    Task<ActivityLogEntry> RecordAsync(long caseId, RecordActivityRequest request, CancellationToken token = default);
    Task<ActivityLogEntry> UpdateAsync(long activityId, UpdateActivityRequest request, CancellationToken token = default);
}

public sealed class SqliteDiscoveryPostureStore(CasePlannerRepository repository) : IDiscoveryPostureStore
{
    public string Provider => "Sqlite";
    public Task<DiscoveryPosture?> GetAsync(long caseId, CancellationToken token = default) => repository.GetDiscoveryPostureAsync(caseId);
    public Task<DiscoveryPosture> SaveAsync(DiscoveryPosture model, CancellationToken token = default) => repository.SaveDiscoveryPostureAsync(model);
}

public sealed class SqlitePipelineHandoffStore(CasePlannerRepository repository) : IPipelineHandoffStore
{
    public string Provider => "Sqlite";
    public Task<List<PipelineHandoffRecord>> GetAsync(long? caseId, CancellationToken token = default) => repository.GetPipelineHandoffsAsync(caseId);
    public Task<PipelineHandoffRecord> SaveAsync(long caseId, PipelineHandoffRequest request, CancellationToken token = default) => repository.SavePipelineHandoffAsync(caseId, request);
}

public sealed class SqliteActivityStore(CasePlannerRepository repository) : IActivityStore
{
    public string Provider => "Sqlite";
    public Task<List<ActivityLogEntry>> GetAsync(long? caseId, CancellationToken token = default) => repository.GetActivityLogAsync(caseId);
    public Task<long?> GetCaseIdAsync(long activityId, CancellationToken token = default) => repository.GetChildCaseIdAsync("activity", activityId);
    public Task<ActivityLogEntry> RecordAsync(long caseId, RecordActivityRequest request, CancellationToken token = default) =>
        repository.RecordActivityAsync(caseId, request.ActivityType, request.Notes, request.OccurredAt);
    public Task<ActivityLogEntry> UpdateAsync(long activityId, UpdateActivityRequest request, CancellationToken token = default) =>
        repository.UpdateActivityEntryAsync(activityId, request);
}

public sealed class SqlServerDiscoveryPostureStore(
    IDatabaseConnectionFactory connections,
    IApplicationActorContext actor,
    SqlServerActivityStore activity) : IDiscoveryPostureStore
{
    public string Provider => "SqlServer";

    public async Task<DiscoveryPosture?> GetAsync(long caseId, CancellationToken token = default)
    {
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectSql + " WHERE case_id=@caseId";
        command.Parameters.Add(new SqlParameter("@caseId", caseId));
        await using var reader = await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) ? Read(reader) : null;
    }

    public async Task<DiscoveryPosture> SaveAsync(DiscoveryPosture model, CancellationToken token = default)
    {
        var previous = await GetAsync(model.CaseId, token);
        var completionChanged = previous is null || previous.IsComplete != model.IsComplete;
        var now = DateTime.UtcNow.ToString("O");
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (previous is null)
        {
            command.CommandText = """
                INSERT INTO dbo.discovery_postures
                    (case_id,strategy,strategy_reason,strategy_selected_date,discovery_served_date,responses_due_date,
                     responses_received_date,responses_reviewed_date,discovery_cutoff_date,planned_depositions,
                     deficiency_status,next_decision,next_review_date,is_complete,created_at,updated_at,
                     completion_changed_at,completion_changed_by,updated_by_user_id,updated_by_display)
                OUTPUT INSERTED.id,INSERTED.row_version
                VALUES(@caseId,@strategy,@reason,@selected,@served,@due,@received,@reviewed,@cutoff,@depositions,
                       @deficiency,@decision,@nextReview,@complete,@now,@now,@completionAt,@completionBy,@actor,@display)
                """;
        }
        else
        {
            command.CommandText = """
                UPDATE dbo.discovery_postures SET strategy=@strategy,strategy_reason=@reason,
                    strategy_selected_date=@selected,discovery_served_date=@served,responses_due_date=@due,
                    responses_received_date=@received,responses_reviewed_date=@reviewed,discovery_cutoff_date=@cutoff,
                    planned_depositions=@depositions,deficiency_status=@deficiency,next_decision=@decision,
                    next_review_date=@nextReview,is_complete=@complete,updated_at=@now,
                    completion_changed_at=CASE WHEN is_complete<>@complete THEN @completionAt ELSE completion_changed_at END,
                    completion_changed_by=CASE WHEN is_complete<>@complete THEN @completionBy ELSE completion_changed_by END,
                    updated_by_user_id=@actor,updated_by_display=@display
                OUTPUT INSERTED.id,INSERTED.row_version
                WHERE case_id=@caseId AND row_version=@version
                """;
            command.Parameters.Add(new SqlParameter("@version", ExpectedVersion(model.RowVersion, "discovery posture", model.CaseId)));
        }
        Bind(command, model, now, completionChanged, actor);
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) throw new WorkItemConcurrencyException("Discovery posture", model.CaseId);
            model.Id = reader.GetInt64(0);
            model.RowVersion = Convert.ToBase64String((byte[])reader.GetValue(1));
        }
        model.CreatedAt ??= now;
        model.UpdatedAt = now;
        if (completionChanged) { model.CompletionChangedAt = now; model.CompletionChangedBy = actor.AuditLabel; }
        await AuditAsync(connection, transaction, model.CaseId, previous is null ? "DiscoveryPostureCreated" : "DiscoveryPostureUpdated", model.Id, token);
        await transaction.CommitAsync(token);
        if (completionChanged)
            await activity.RecordAsync(model.CaseId, model.IsComplete ? "DiscoveryCompleted" : "DiscoveryReopened",
                model.IsComplete ? "Discovery marked complete." : "Discovery marked incomplete.", null, token);
        return model;
    }

    private const string SelectSql = """
        SELECT id,case_id,strategy,strategy_reason,strategy_selected_date,discovery_served_date,
               responses_due_date,responses_received_date,responses_reviewed_date,discovery_cutoff_date,
               planned_depositions,deficiency_status,next_decision,next_review_date,is_complete,created_at,
               updated_at,completion_changed_at,completion_changed_by,row_version
        FROM dbo.discovery_postures
        """;

    private static DiscoveryPosture Read(DbDataReader reader) => new()
    {
        Id=reader.GetInt64(0),CaseId=reader.GetInt64(1),Strategy=Text(reader,2)??"Strategy not selected",
        StrategyReason=Text(reader,3),StrategySelectedDate=Date(Text(reader,4)),DiscoveryServedDate=Date(Text(reader,5)),
        ResponsesDueDate=Date(Text(reader,6)),ResponsesReceivedDate=Date(Text(reader,7)),ResponsesReviewedDate=Date(Text(reader,8)),
        DiscoveryCutoffDate=Date(Text(reader,9)),PlannedDepositions=Text(reader,10),DeficiencyStatus=Text(reader,11),
        NextDecision=Text(reader,12),NextReviewDate=Date(Text(reader,13)),IsComplete=Bool(reader,14),
        CreatedAt=Text(reader,15),UpdatedAt=Text(reader,16),CompletionChangedAt=Text(reader,17),
        CompletionChangedBy=Text(reader,18),RowVersion=Convert.ToBase64String((byte[])reader.GetValue(19))
    };

    private static void Bind(DbCommand command, DiscoveryPosture model, string now, bool completionChanged, IApplicationActorContext actor)
    {
        command.Parameters.Add(new SqlParameter("@caseId",model.CaseId));command.Parameters.Add(new SqlParameter("@strategy",string.IsNullOrWhiteSpace(model.Strategy)?"Strategy not selected":model.Strategy));
        command.Parameters.Add(new SqlParameter("@reason",Db(model.StrategyReason)));command.Parameters.Add(new SqlParameter("@selected",Db(Date(model.StrategySelectedDate))));
        command.Parameters.Add(new SqlParameter("@served",Db(Date(model.DiscoveryServedDate))));command.Parameters.Add(new SqlParameter("@due",Db(Date(model.ResponsesDueDate))));
        command.Parameters.Add(new SqlParameter("@received",Db(Date(model.ResponsesReceivedDate))));command.Parameters.Add(new SqlParameter("@reviewed",Db(Date(model.ResponsesReviewedDate))));
        command.Parameters.Add(new SqlParameter("@cutoff",Db(Date(model.DiscoveryCutoffDate))));command.Parameters.Add(new SqlParameter("@depositions",Db(model.PlannedDepositions)));
        command.Parameters.Add(new SqlParameter("@deficiency",Db(model.DeficiencyStatus)));command.Parameters.Add(new SqlParameter("@decision",Db(model.NextDecision)));
        command.Parameters.Add(new SqlParameter("@nextReview",Db(Date(model.NextReviewDate))));command.Parameters.Add(new SqlParameter("@complete",model.IsComplete));
        command.Parameters.Add(new SqlParameter("@now",now));command.Parameters.Add(new SqlParameter("@completionAt",completionChanged?now:Db(model.CompletionChangedAt)));
        command.Parameters.Add(new SqlParameter("@completionBy",completionChanged?actor.AuditLabel:Db(model.CompletionChangedBy)));
        command.Parameters.Add(new SqlParameter("@actor",(object?)actor.UserId??DBNull.Value));command.Parameters.Add(new SqlParameter("@display",actor.AuditLabel));
    }

    private async Task AuditAsync(DbConnection connection,DbTransaction transaction,long caseId,string action,long id,CancellationToken token)
    {
        await using var audit=connection.CreateCommand();audit.Transaction=transaction;
        audit.CommandText="INSERT INTO dbo.audit_events(case_id,actor_user_id,action,entity_type,entity_id) VALUES(@caseId,@actor,@action,'DiscoveryPosture',@id)";
        audit.Parameters.Add(new SqlParameter("@caseId",caseId));audit.Parameters.Add(new SqlParameter("@actor",(object?)actor.UserId??DBNull.Value));
        audit.Parameters.Add(new SqlParameter("@action",action));audit.Parameters.Add(new SqlParameter("@id",id.ToString()));await audit.ExecuteNonQueryAsync(token);
    }

    private static byte[] ExpectedVersion(string? value,string kind,long id){if(string.IsNullOrWhiteSpace(value))throw new ArgumentException($"RowVersion is required when changing SQL Server {kind} {id}.");try{return Convert.FromBase64String(value);}catch(FormatException){throw new ArgumentException($"The {kind} RowVersion is not valid.");}}
    private static string? Text(DbDataReader reader,int i)=>reader.IsDBNull(i)?null:Convert.ToString(reader.GetValue(i));
    private static bool Bool(DbDataReader reader,int i)=>!reader.IsDBNull(i)&&Convert.ToBoolean(reader.GetValue(i));
    private static string? Date(string? value)=>DateOnly.TryParse(value,out var date)?date.ToString("yyyy-MM-dd"):null;
    private static object Db(string? value)=>string.IsNullOrWhiteSpace(value)?DBNull.Value:value;
}

public sealed class SqlServerPipelineHandoffStore(
    IDatabaseConnectionFactory connections,
    IApplicationActorContext actor) : IPipelineHandoffStore
{
    public string Provider => "SqlServer";

    public async Task<List<PipelineHandoffRecord>> GetAsync(long? caseId, CancellationToken token = default)
    {
        var result=new List<PipelineHandoffRecord>();await using var connection=connections.CreateConnection();await connection.OpenAsync(token);
        await using var command=connection.CreateCommand();command.CommandText="""
            SELECT id,case_id,previous_holder,new_holder,previous_stage,new_stage,handoff_date,next_review_date,
                   note,created_at,created_by_display,row_version
            FROM dbo.pipeline_handoffs WHERE (@caseId IS NULL OR case_id=@caseId) ORDER BY created_at DESC,id DESC
            """;command.Parameters.Add(new SqlParameter("@caseId",(object?)caseId??DBNull.Value));await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token))result.Add(new(){Id=reader.GetInt64(0),CaseId=reader.GetInt64(1),PreviousHolder=Text(reader,2),
            NewHolder=Text(reader,3)??"",PreviousStage=Text(reader,4),NewStage=Text(reader,5)??"",HandoffDate=Date(Text(reader,6)),
            NextReviewDate=Date(Text(reader,7)),Note=Text(reader,8),CreatedAt=Text(reader,9),CreatedBy=Text(reader,10),
            RowVersion=Convert.ToBase64String((byte[])reader.GetValue(11))});return result;
    }

    public async Task<PipelineHandoffRecord> SaveAsync(long caseId, PipelineHandoffRequest request, CancellationToken token = default)
    {
        var expected=ExpectedVersion(request.RowVersion,caseId);var now=DateTime.UtcNow.ToString("O");
        var handoffDate=Date(request.HandoffDate)??DateTime.UtcNow.ToString("yyyy-MM-dd");
        await using var connection=connections.CreateConnection();await connection.OpenAsync(token);await using var transaction=await connection.BeginTransactionAsync(token);
        string? previousHolder;string? previousStage;
        await using(var read=connection.CreateCommand()){read.Transaction=transaction;read.CommandText="SELECT current_holder,pipeline_stage FROM dbo.cases WHERE id=@id AND row_version=@version AND is_deleted=0";read.Parameters.Add(new SqlParameter("@id",caseId));read.Parameters.Add(new SqlParameter("@version",expected));await using var reader=await read.ExecuteReaderAsync(token);if(!await reader.ReadAsync(token))throw new CaseConcurrencyException(caseId);previousHolder=Text(reader,0);previousStage=Text(reader,1);}
        long id;string rowVersion;
        await using(var insert=connection.CreateCommand()){insert.Transaction=transaction;insert.CommandText="""
            INSERT INTO dbo.pipeline_handoffs(case_id,previous_holder,new_holder,previous_stage,new_stage,handoff_date,
                next_review_date,note,created_at,created_by_user_id,created_by_display)
            OUTPUT INSERTED.id,INSERTED.row_version
            VALUES(@caseId,@previousHolder,@newHolder,@previousStage,@newStage,@handoff,@review,@note,@now,@actor,@display)
            """;insert.Parameters.Add(new SqlParameter("@caseId",caseId));insert.Parameters.Add(new SqlParameter("@previousHolder",Db(previousHolder)));
            insert.Parameters.Add(new SqlParameter("@newHolder",request.NewHolder??""));insert.Parameters.Add(new SqlParameter("@previousStage",Db(previousStage)));
            insert.Parameters.Add(new SqlParameter("@newStage",request.NewStage??""));insert.Parameters.Add(new SqlParameter("@handoff",handoffDate));
            insert.Parameters.Add(new SqlParameter("@review",Db(Date(request.NextReviewDate))));insert.Parameters.Add(new SqlParameter("@note",Db(request.Note)));
            insert.Parameters.Add(new SqlParameter("@now",now));insert.Parameters.Add(new SqlParameter("@actor",(object?)actor.UserId??DBNull.Value));
            insert.Parameters.Add(new SqlParameter("@display",actor.AuditLabel));await using var reader=await insert.ExecuteReaderAsync(token);await reader.ReadAsync(token);id=reader.GetInt64(0);rowVersion=Convert.ToBase64String((byte[])reader.GetValue(1));}
        string caseVersion;
        await using(var update=connection.CreateCommand()){update.Transaction=transaction;update.CommandText="""
            UPDATE dbo.cases SET current_holder=@holder,pipeline_stage=@stage,date_sent_to_current_holder=@handoff,
                next_review_date=@review,updated_at=@now OUTPUT INSERTED.row_version
            WHERE id=@id AND row_version=@version AND is_deleted=0
            """;update.Parameters.Add(new SqlParameter("@holder",request.NewHolder??""));update.Parameters.Add(new SqlParameter("@stage",request.NewStage??""));
            update.Parameters.Add(new SqlParameter("@handoff",handoffDate));update.Parameters.Add(new SqlParameter("@review",Db(Date(request.NextReviewDate))));
            update.Parameters.Add(new SqlParameter("@now",now));update.Parameters.Add(new SqlParameter("@id",caseId));update.Parameters.Add(new SqlParameter("@version",expected));
            var value=await update.ExecuteScalarAsync(token);if(value is null)throw new CaseConcurrencyException(caseId);caseVersion=Convert.ToBase64String((byte[])value);}
        await using(var audit=connection.CreateCommand()){audit.Transaction=transaction;audit.CommandText="INSERT INTO dbo.audit_events(case_id,actor_user_id,action,entity_type,entity_id) VALUES(@caseId,@actor,'PipelineHandoffCreated','PipelineHandoff',@id)";audit.Parameters.Add(new SqlParameter("@caseId",caseId));audit.Parameters.Add(new SqlParameter("@actor",(object?)actor.UserId??DBNull.Value));audit.Parameters.Add(new SqlParameter("@id",id.ToString()));await audit.ExecuteNonQueryAsync(token);}
        await transaction.CommitAsync(token);
        return new(){Id=id,CaseId=caseId,PreviousHolder=previousHolder,NewHolder=request.NewHolder??"",PreviousStage=previousStage,
            NewStage=request.NewStage??"",HandoffDate=handoffDate,NextReviewDate=Date(request.NextReviewDate),Note=request.Note,
            CreatedAt=now,CreatedBy=actor.AuditLabel,RowVersion=rowVersion,CaseRowVersion=caseVersion};
    }

    private static byte[] ExpectedVersion(string? value,long id){if(string.IsNullOrWhiteSpace(value))throw new ArgumentException($"RowVersion is required when changing SQL Server case {id}.");try{return Convert.FromBase64String(value);}catch(FormatException){throw new ArgumentException("RowVersion is not a valid concurrency token.");}}
    private static string? Text(DbDataReader reader,int i)=>reader.IsDBNull(i)?null:Convert.ToString(reader.GetValue(i));
    private static string? Date(string? value)=>DateOnly.TryParse(value,out var date)?date.ToString("yyyy-MM-dd"):null;
    private static object Db(string? value)=>string.IsNullOrWhiteSpace(value)?DBNull.Value:value;
}

// Shared by the SQL Server case-save (SqlServerCaseCatalogReader) and quick-action holder-set
// (SqlServerCaseQuickActionService) paths, which - unlike SqlServerPipelineHandoffStore.SaveAsync
// above - have no dedicated "handoff" concept (no note, no scheduled next-review date) and must
// not duplicate what that endpoint already logs for its own callers.
internal static class PipelineHandoffTransitionLogger
{
    public static async Task RecordIfChangedAsync(
        DbConnection connection, DbTransaction transaction, long caseId,
        string? previousHolder, string? newHolder, string? previousStage, string? newStage,
        Guid? actorUserId, string? actorDisplay, CancellationToken token = default)
    {
        var holderChanged = (previousHolder ?? "") != (newHolder ?? "");
        var stageChanged = (previousStage ?? "") != (newStage ?? "");
        if (!holderChanged && !stageChanged) return;

        var now = DateTime.UtcNow.ToString("O");
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO dbo.pipeline_handoffs(case_id,previous_holder,new_holder,previous_stage,new_stage,handoff_date,
                next_review_date,note,created_at,created_by_user_id,created_by_display)
            VALUES(@caseId,@previousHolder,@newHolder,@previousStage,@newStage,@handoff,@review,@note,@now,@actor,@display)
            """;
        insert.Parameters.Add(new SqlParameter("@caseId", caseId));
        insert.Parameters.Add(new SqlParameter("@previousHolder", (object?)previousHolder ?? DBNull.Value));
        insert.Parameters.Add(new SqlParameter("@newHolder", newHolder ?? ""));
        insert.Parameters.Add(new SqlParameter("@previousStage", (object?)previousStage ?? DBNull.Value));
        insert.Parameters.Add(new SqlParameter("@newStage", newStage ?? ""));
        insert.Parameters.Add(new SqlParameter("@handoff", now[..10]));
        insert.Parameters.Add(new SqlParameter("@review", DBNull.Value));
        insert.Parameters.Add(new SqlParameter("@note", DBNull.Value));
        insert.Parameters.Add(new SqlParameter("@now", now));
        insert.Parameters.Add(new SqlParameter("@actor", (object?)actorUserId ?? DBNull.Value));
        insert.Parameters.Add(new SqlParameter("@display", (object?)actorDisplay ?? DBNull.Value));
        await insert.ExecuteNonQueryAsync(token);
    }
}

public sealed class SqlServerActivityService(SqlServerActivityStore store) : IActivityStore
{
    public string Provider => "SqlServer";
    public Task<List<ActivityLogEntry>> GetAsync(long? caseId,CancellationToken token=default)=>store.GetAsync(caseId,token);
    public Task<long?> GetCaseIdAsync(long activityId,CancellationToken token=default)=>store.GetCaseIdAsync(activityId,token);
    public Task<ActivityLogEntry> RecordAsync(long caseId,RecordActivityRequest request,CancellationToken token=default)=>
        store.RecordAsync(caseId,request.ActivityType,request.Notes,request.OccurredAt,token);
    public Task<ActivityLogEntry> UpdateAsync(long activityId,UpdateActivityRequest request,CancellationToken token=default)=>
        store.UpdateAsync(activityId,request,request.RowVersion,token);
}
