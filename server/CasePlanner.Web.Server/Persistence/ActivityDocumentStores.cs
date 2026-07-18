using System.Globalization;
using System.Data.Common;
using CasePlanner.Data;
using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Security;
using CasePlanner.Web.Server.Services;
using Microsoft.Data.SqlClient;

namespace CasePlanner.Web.Server.Persistence;

public sealed class SqlServerActivityStore(IDatabaseConnectionFactory connections,IApplicationActorContext actor)
{
    private static readonly HashSet<string> MeaningfulTypes=new(StringComparer.Ordinal)
    {"ComplaintFiled","AnswerFiled","ServiceCompleted","PublicationCompleted","DiscoveryServed","DiscoveryResponsesReceived","DiscoveryResponsesReviewed","DepositionHeld","AppraisalReceived","AppraisalReviewed","NegotiationPositionChanged","SettlementAuthorityRequested","SettlementAuthorityReceived","MotionFiled","MotionDecided","MediationScheduled","MediationHeld","TrialPrepMilestoneCompleted","AttorneyStrategyDecisionRecorded"};
    public async Task<List<ActivityLogEntry>> GetAsync(long? caseId, CancellationToken token = default)
    {
        var result = new List<ActivityLogEntry>();
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,case_id,activity_type,is_meaningful,occurred_at,notes,created_at,
                   actor_user_id,actor_display,row_version
            FROM dbo.activity_log
            WHERE @caseId IS NULL OR case_id=@caseId
            ORDER BY occurred_at DESC,id DESC
            """;
        command.Parameters.Add(new SqlParameter("@caseId", (object?)caseId ?? DBNull.Value));
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
            {
                result.Add(new ActivityLogEntry
                {
                    Id = reader.GetInt64(0), CaseId = reader.GetInt64(1),
                    ActivityType = Text(reader, 2) ?? "Other", IsMeaningful = Bool(reader, 3),
                    OccurredAt = Text(reader, 4) ?? "", Notes = Text(reader, 5), CreatedAt = Text(reader, 6),
                    ActorUserId = Text(reader, 7), ActorDisplay = Text(reader, 8),
                    RowVersion = Convert.ToBase64String((byte[])reader.GetValue(9))
                });
            }
        }

        if (result.Count == 0) return result;
        var byId = result.ToDictionary(x => x.Id);
        await using var history = connection.CreateCommand();
        history.CommandText = """
            SELECT h.id,h.activity_id,h.previous_type,h.new_type,h.previous_occurred_at,
                   h.new_occurred_at,h.previous_notes,h.new_notes,h.reason,h.created_at,
                   h.edited_by_user_id,h.edited_by_display
            FROM dbo.activity_log_history h
            JOIN dbo.activity_log a ON a.id=h.activity_id
            WHERE @caseId IS NULL OR a.case_id=@caseId
            ORDER BY h.created_at,h.id
            """;
        history.Parameters.Add(new SqlParameter("@caseId", (object?)caseId ?? DBNull.Value));
        await using var historyReader = await history.ExecuteReaderAsync(token);
        while (await historyReader.ReadAsync(token))
        {
            var activityId = historyReader.GetInt64(1);
            if (!byId.TryGetValue(activityId, out var entry)) continue;
            entry.History.Add(new ActivityLogHistoryEntry
            {
                Id=historyReader.GetInt64(0),ActivityId=activityId,PreviousType=Text(historyReader,2),
                NewType=Text(historyReader,3),PreviousOccurredAt=Text(historyReader,4),NewOccurredAt=Text(historyReader,5),
                PreviousNotes=Text(historyReader,6),NewNotes=Text(historyReader,7),Reason=Text(historyReader,8),
                CreatedAt=Text(historyReader,9),EditedByUserId=Text(historyReader,10),EditedByDisplay=Text(historyReader,11)
            });
        }
        return result;
    }

    public async Task<long?> GetCaseIdAsync(long id,CancellationToken token=default)
    {
        await using var connection=connections.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();
        command.CommandText="SELECT case_id FROM dbo.activity_log WHERE id=@id";command.Parameters.Add(new SqlParameter("@id",id));
        var value=await command.ExecuteScalarAsync(token);return value is null?null:Convert.ToInt64(value);
    }

    public async Task<ActivityLogEntry> RecordAsync(long caseId,string activityType,string? notes,string? occurredAt,CancellationToken token=default)
    {
        activityType=string.IsNullOrWhiteSpace(activityType)?"Other":activityType;var meaningful=MeaningfulTypes.Contains(activityType);var now=DateTime.UtcNow.ToString("O");var occurred=string.IsNullOrWhiteSpace(occurredAt)?now:occurredAt;
        await using var connection=connections.CreateConnection();await connection.OpenAsync(token);await using var transaction=await connection.BeginTransactionAsync(token);
        await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="INSERT INTO dbo.activity_log(case_id,activity_type,is_meaningful,occurred_at,notes,created_at,actor_user_id,actor_display) OUTPUT INSERTED.id,INSERTED.row_version VALUES(@caseId,@type,@meaningful,@occurred,@notes,@now,@actor,@display)";
        command.Parameters.Add(new SqlParameter("@caseId",caseId));command.Parameters.Add(new SqlParameter("@type",activityType));command.Parameters.Add(new SqlParameter("@meaningful",meaningful));command.Parameters.Add(new SqlParameter("@occurred",occurred));command.Parameters.Add(new SqlParameter("@notes",Db(notes)));command.Parameters.Add(new SqlParameter("@now",now));command.Parameters.Add(new SqlParameter("@actor",(object?)actor.UserId??DBNull.Value));command.Parameters.Add(new SqlParameter("@display",actor.AuditLabel));
        long id;string version;try{await using var reader=await command.ExecuteReaderAsync(token);await reader.ReadAsync(token);id=reader.GetInt64(0);version=Convert.ToBase64String((byte[])reader.GetValue(1));}catch(SqlException ex)when(ex.Number==547){throw new InvalidOperationException($"SQL Server case {caseId} does not exist.");}
        if(meaningful){await using var update=connection.CreateCommand();update.Transaction=transaction;update.CommandText="UPDATE dbo.cases SET last_meaningful_activity_date=@occurred WHERE id=@caseId AND (last_meaningful_activity_date IS NULL OR last_meaningful_activity_date<@occurred)";update.Parameters.Add(new SqlParameter("@occurred",occurred));update.Parameters.Add(new SqlParameter("@caseId",caseId));await update.ExecuteNonQueryAsync(token);}
        await AuditAsync(connection,transaction,caseId,"ActivityCreated",id,token);await transaction.CommitAsync(token);
        return new(){Id=id,CaseId=caseId,ActivityType=activityType,IsMeaningful=meaningful,OccurredAt=occurred,Notes=notes,CreatedAt=now,ActorUserId=actor.UserId?.ToString("D"),ActorDisplay=actor.AuditLabel,RowVersion=version};
    }

    public async Task<ActivityLogEntry> UpdateAsync(long id,UpdateActivityRequest request,string? rowVersion,CancellationToken token=default)
    {
        var expected=ExpectedVersion(rowVersion,"activity",id);await using var connection=connections.CreateConnection();await connection.OpenAsync(token);await using var transaction=await connection.BeginTransactionAsync(token);
        long caseId;string oldType;string oldOccurred;string? oldNotes;string? creatorId;string? creatorDisplay;
        await using(var read=connection.CreateCommand()){read.Transaction=transaction;read.CommandText="SELECT case_id,activity_type,occurred_at,notes,actor_user_id,actor_display FROM dbo.activity_log WHERE id=@id";read.Parameters.Add(new SqlParameter("@id",id));await using var reader=await read.ExecuteReaderAsync(token);if(!await reader.ReadAsync(token))throw new InvalidOperationException("Activity entry not found.");caseId=reader.GetInt64(0);oldType=Text(reader,1)??"Other";oldOccurred=Text(reader,2)??"";oldNotes=Text(reader,3);creatorId=Text(reader,4);creatorDisplay=Text(reader,5);}
        var newType=string.IsNullOrWhiteSpace(request.ActivityType)?"Other":request.ActivityType;var newOccurred=string.IsNullOrWhiteSpace(request.OccurredAt)?oldOccurred:request.OccurredAt;var newNotes=string.IsNullOrWhiteSpace(request.Notes)?null:request.Notes;var meaningful=MeaningfulTypes.Contains(newType);var now=DateTime.UtcNow.ToString("O");
        await using(var history=connection.CreateCommand()){history.Transaction=transaction;history.CommandText="INSERT INTO dbo.activity_log_history(activity_id,previous_type,new_type,previous_occurred_at,new_occurred_at,previous_notes,new_notes,reason,created_at,edited_by_user_id,edited_by_display) VALUES(@id,@oldType,@newType,@oldOccurred,@newOccurred,@oldNotes,@newNotes,@reason,@now,@actor,@display)";history.Parameters.Add(new SqlParameter("@id",id));history.Parameters.Add(new SqlParameter("@oldType",oldType));history.Parameters.Add(new SqlParameter("@newType",newType));history.Parameters.Add(new SqlParameter("@oldOccurred",oldOccurred));history.Parameters.Add(new SqlParameter("@newOccurred",newOccurred));history.Parameters.Add(new SqlParameter("@oldNotes",Db(oldNotes)));history.Parameters.Add(new SqlParameter("@newNotes",Db(newNotes)));history.Parameters.Add(new SqlParameter("@reason",Db(request.Reason)));history.Parameters.Add(new SqlParameter("@now",now));history.Parameters.Add(new SqlParameter("@actor",(object?)actor.UserId??DBNull.Value));history.Parameters.Add(new SqlParameter("@display",actor.AuditLabel));await history.ExecuteNonQueryAsync(token);}
        string nextVersion;await using(var update=connection.CreateCommand()){update.Transaction=transaction;update.CommandText="UPDATE dbo.activity_log SET activity_type=@type,is_meaningful=@meaningful,occurred_at=@occurred,notes=@notes OUTPUT INSERTED.row_version WHERE id=@id AND row_version=@version";update.Parameters.Add(new SqlParameter("@type",newType));update.Parameters.Add(new SqlParameter("@meaningful",meaningful));update.Parameters.Add(new SqlParameter("@occurred",newOccurred));update.Parameters.Add(new SqlParameter("@notes",Db(newNotes)));update.Parameters.Add(new SqlParameter("@id",id));update.Parameters.Add(new SqlParameter("@version",expected));var value=await update.ExecuteScalarAsync(token);if(value is null)throw new WorkItemConcurrencyException("Activity",id);nextVersion=Convert.ToBase64String((byte[])value);}
        await using(var recompute=connection.CreateCommand()){recompute.Transaction=transaction;recompute.CommandText="UPDATE dbo.cases SET last_meaningful_activity_date=(SELECT MAX(occurred_at) FROM dbo.activity_log WHERE case_id=@caseId AND is_meaningful=1) WHERE id=@caseId";recompute.Parameters.Add(new SqlParameter("@caseId",caseId));await recompute.ExecuteNonQueryAsync(token);}
        await AuditAsync(connection,transaction,caseId,"ActivityUpdated",id,token);await transaction.CommitAsync(token);return new(){Id=id,CaseId=caseId,ActivityType=newType,IsMeaningful=meaningful,OccurredAt=newOccurred,Notes=newNotes,ActorUserId=creatorId,ActorDisplay=creatorDisplay,RowVersion=nextVersion};
    }

    private async Task AuditAsync(DbConnection connection,DbTransaction transaction,long caseId,string action,long id,CancellationToken token){await using var audit=connection.CreateCommand();audit.Transaction=transaction;audit.CommandText="INSERT INTO dbo.audit_events(case_id,actor_user_id,action,entity_type,entity_id) VALUES(@caseId,@actor,@action,'Activity',@id)";audit.Parameters.Add(new SqlParameter("@caseId",caseId));audit.Parameters.Add(new SqlParameter("@actor",(object?)actor.UserId??DBNull.Value));audit.Parameters.Add(new SqlParameter("@action",action));audit.Parameters.Add(new SqlParameter("@id",id));await audit.ExecuteNonQueryAsync(token);}
    private static object Db(string? value)=>string.IsNullOrWhiteSpace(value)?DBNull.Value:value;
    private static byte[] ExpectedVersion(string? value,string kind,long id){if(string.IsNullOrWhiteSpace(value))throw new ArgumentException($"RowVersion is required when changing SQL Server {kind} {id}.");try{return Convert.FromBase64String(value);}catch(FormatException){throw new ArgumentException($"The {kind} RowVersion is not valid.");}}

    private static string? Text(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    private static bool Bool(DbDataReader reader, int ordinal) =>
        !reader.IsDBNull(ordinal) && Convert.ToBoolean(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
}

public sealed class SqlServerDocumentExportStore(IDatabaseConnectionFactory connections,IApplicationActorContext actor)
{
    public async Task<List<DocumentExportRecord>> GetAsync(long? caseId, CancellationToken token = default)
    {
        var result = new List<DocumentExportRecord>();
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,case_id,document_type,document_title,output_path,created_at,status,qa_status,
                   qa_notes,error_message,content_text,base_template_version,issue_tag_versions,is_draft,
                   is_finalized,merge_field_values,created_by_user_id,created_by_display,
                   qa_reviewed_by_user_id,qa_reviewed_by_display,row_version
            FROM dbo.document_exports
            WHERE is_deleted=0 AND (@caseId IS NULL OR case_id=@caseId)
            ORDER BY created_at DESC,id DESC
            """;
        command.Parameters.Add(new SqlParameter("@caseId", (object?)caseId ?? DBNull.Value));
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            result.Add(new DocumentExportRecord
            {
                Id=reader.GetInt64(0),CaseId=reader.GetInt64(1),DocumentType=Text(reader,2)??"",
                DocumentTitle=Text(reader,3)??"",OutputPath=Text(reader,4)??"",CreatedAt=Text(reader,5)??"",
                Status=Text(reader,6)??"Generated",QaStatus=Text(reader,7)??"Not Reviewed",QaNotes=Text(reader,8),
                ErrorMessage=Text(reader,9),ContentText=Text(reader,10),BaseTemplateVersion=Text(reader,11),
                IssueTagVersions=Text(reader,12),IsDraft=Bool(reader,13),IsFinalized=Bool(reader,14),
                MergeFieldValues=Text(reader,15),CreatedByUserId=Text(reader,16),CreatedByDisplay=Text(reader,17),
                QaReviewedByUserId=Text(reader,18),QaReviewedByDisplay=Text(reader,19),
                RowVersion=Convert.ToBase64String((byte[])reader.GetValue(20))
            });
        }
        return result;
    }

    public async Task<DocumentExportRecord> SaveGeneratedAsync(DocumentExportRecord model,CancellationToken token=default)
    {
        if(model.Id!=0)throw new ArgumentException("Generated document metadata must be inserted as a new row.");var now=DateTime.UtcNow.ToString("O");model.CreatedAt=now;model.Status="Generated";model.QaStatus="Not Reviewed";model.CreatedByUserId=actor.UserId?.ToString("D");model.CreatedByDisplay=actor.AuditLabel;
        await using var connection=connections.CreateConnection();await connection.OpenAsync(token);await using var transaction=await connection.BeginTransactionAsync(token);await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="INSERT INTO dbo.document_exports(case_id,document_type,document_title,output_path,created_at,status,qa_status,qa_notes,error_message,content_text,base_template_version,issue_tag_versions,is_draft,is_finalized,merge_field_values,created_by_user_id,created_by_display,is_deleted) OUTPUT INSERTED.id,INSERTED.row_version VALUES(@caseId,@type,@title,@path,@now,'Generated','Not Reviewed',NULL,NULL,@content,@base,@tags,@draft,@final,@merge,@actor,@display,0)";
        command.Parameters.Add(new SqlParameter("@caseId",model.CaseId));command.Parameters.Add(new SqlParameter("@type",model.DocumentType??""));command.Parameters.Add(new SqlParameter("@title",model.DocumentTitle??""));command.Parameters.Add(new SqlParameter("@path",model.OutputPath??""));command.Parameters.Add(new SqlParameter("@now",now));command.Parameters.Add(new SqlParameter("@content",Db(model.ContentText)));command.Parameters.Add(new SqlParameter("@base",Db(model.BaseTemplateVersion)));command.Parameters.Add(new SqlParameter("@tags",Db(model.IssueTagVersions)));command.Parameters.Add(new SqlParameter("@draft",model.IsDraft));command.Parameters.Add(new SqlParameter("@final",model.IsFinalized));command.Parameters.Add(new SqlParameter("@merge",Db(model.MergeFieldValues)));command.Parameters.Add(new SqlParameter("@actor",(object?)actor.UserId??DBNull.Value));command.Parameters.Add(new SqlParameter("@display",actor.AuditLabel));
        try{await using var reader=await command.ExecuteReaderAsync(token);await reader.ReadAsync(token);model.Id=reader.GetInt64(0);model.RowVersion=Convert.ToBase64String((byte[])reader.GetValue(1));}catch(SqlException ex)when(ex.Number==547){throw new InvalidOperationException($"SQL Server case {model.CaseId} does not exist.");}
        await AuditAsync(connection,transaction,model.CaseId,"DocumentGenerated",model.Id,token);await transaction.CommitAsync(token);return model;
    }

    public async Task<DocumentExportRecord> SaveQaAsync(long id,string qaStatus,string? qaNotes,string? rowVersion,CancellationToken token=default)
    {
        var expected=ExpectedVersion(rowVersion,"document export",id);await using var connection=connections.CreateConnection();await connection.OpenAsync(token);await using var transaction=await connection.BeginTransactionAsync(token);long caseId;string version;
        await using(var command=connection.CreateCommand()){command.Transaction=transaction;command.CommandText="UPDATE dbo.document_exports SET qa_status=@status,qa_notes=@notes,qa_reviewed_by_user_id=@actor,qa_reviewed_by_display=@display OUTPUT INSERTED.case_id,INSERTED.row_version WHERE id=@id AND row_version=@version AND is_deleted=0";command.Parameters.Add(new SqlParameter("@status",string.IsNullOrWhiteSpace(qaStatus)?"Not Reviewed":qaStatus));command.Parameters.Add(new SqlParameter("@notes",Db(qaNotes)));command.Parameters.Add(new SqlParameter("@actor",(object?)actor.UserId??DBNull.Value));command.Parameters.Add(new SqlParameter("@display",actor.AuditLabel));command.Parameters.Add(new SqlParameter("@id",id));command.Parameters.Add(new SqlParameter("@version",expected));await using var reader=await command.ExecuteReaderAsync(token);if(!await reader.ReadAsync(token))throw new WorkItemConcurrencyException("Document export",id);caseId=reader.GetInt64(0);version=Convert.ToBase64String((byte[])reader.GetValue(1));}
        await AuditAsync(connection,transaction,caseId,"DocumentQaUpdated",id,token);await transaction.CommitAsync(token);var saved=(await GetAsync(caseId,token)).Single(x=>x.Id==id);saved.RowVersion=version;return saved;
    }

    public async Task SoftDeleteAsync(long id,string? rowVersion,CancellationToken token=default)
    {
        var expected=ExpectedVersion(rowVersion,"document export",id);await using var connection=connections.CreateConnection();await connection.OpenAsync(token);await using var transaction=await connection.BeginTransactionAsync(token);await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="UPDATE dbo.document_exports SET is_deleted=1,deleted_utc=SYSUTCDATETIME(),deleted_by_user_id=@actor OUTPUT INSERTED.case_id WHERE id=@id AND row_version=@version AND is_deleted=0";command.Parameters.Add(new SqlParameter("@actor",(object?)actor.UserId??DBNull.Value));command.Parameters.Add(new SqlParameter("@id",id));command.Parameters.Add(new SqlParameter("@version",expected));var value=await command.ExecuteScalarAsync(token);if(value is null)throw new WorkItemConcurrencyException("Document export",id);await AuditAsync(connection,transaction,Convert.ToInt64(value),"DocumentDeleted",id,token);await transaction.CommitAsync(token);
    }

    private async Task AuditAsync(DbConnection connection,DbTransaction transaction,long caseId,string action,long id,CancellationToken token){await using var audit=connection.CreateCommand();audit.Transaction=transaction;audit.CommandText="INSERT INTO dbo.audit_events(case_id,actor_user_id,action,entity_type,entity_id) VALUES(@caseId,@actor,@action,'DocumentExport',@id)";audit.Parameters.Add(new SqlParameter("@caseId",caseId));audit.Parameters.Add(new SqlParameter("@actor",(object?)actor.UserId??DBNull.Value));audit.Parameters.Add(new SqlParameter("@action",action));audit.Parameters.Add(new SqlParameter("@id",id));await audit.ExecuteNonQueryAsync(token);}
    private static object Db(string? value)=>string.IsNullOrWhiteSpace(value)?DBNull.Value:value;
    private static byte[] ExpectedVersion(string? value,string kind,long id){if(string.IsNullOrWhiteSpace(value))throw new ArgumentException($"RowVersion is required when changing SQL Server {kind} {id}.");try{return Convert.FromBase64String(value);}catch(FormatException){throw new ArgumentException($"The {kind} RowVersion is not valid.");}}

    private static string? Text(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    private static bool Bool(DbDataReader reader, int ordinal) =>
        !reader.IsDBNull(ordinal) && Convert.ToBoolean(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
}

public sealed class SqlServerDocumentPilotService(IDocumentStorage documents,SqlServerDocumentExportStore store)
{
    public async Task<DocumentExportRecord> GenerateTextAsync(long caseId,SaveGeneratedDocumentRequest request,CancellationToken token=default)
    {
        if(string.IsNullOrWhiteSpace(request.Title))throw new ArgumentException("Document title is required.");if(string.IsNullOrWhiteSpace(request.Text))throw new ArgumentException("Document content is required.");var stamp=DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");var fileName=$"{stamp}_{Clean(request.Title)}.txt";var path=documents.CreatePath(caseId,fileName);await documents.WriteTextAsync(path,request.Text,token);
        try{return await store.SaveGeneratedAsync(new(){CaseId=caseId,DocumentType=string.IsNullOrWhiteSpace(request.Kind)?"Generated":request.Kind,DocumentTitle=request.Title,OutputPath=path,ContentText=request.Text,BaseTemplateVersion=request.BaseTemplateVersion,IssueTagVersions=request.IssueTagVersions,MergeFieldValues=request.MergeFieldValues,IsDraft=request.IsDraft,IsFinalized=request.IsFinalized},token);}catch{await documents.DeleteIfExistsAsync(path,token);throw;}
    }
    private static string Clean(string value){var invalid=Path.GetInvalidFileNameChars();var chars=value.Select(c=>invalid.Contains(c)?'_':c).ToArray();var result=new string(chars).Trim();return string.IsNullOrWhiteSpace(result)?"Document":result;}
}
