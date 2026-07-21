using System.Data.Common;
using CasePlanner.Data;
using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Security;
using CasePlanner.Web.Server.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace CasePlanner.Web.Server.Persistence;

public sealed class WorkItemConcurrencyException(string kind, long id)
    : Exception($"{kind} {id} was changed by another user. Reload it before saving again.");

public abstract class SqlServerWorkItemStoreBase(IDatabaseConnectionFactory connections, IHttpContextAccessor httpContextAccessor)
{
    protected IDatabaseConnectionFactory Connections { get; } = connections;

    protected Guid? ActorUserId =>
        httpContextAccessor.HttpContext?.Items[EntraUserProvisioningMiddleware.ProfileItemKey] is AuthenticatedUserProfile profile ? profile.Id : null;

    protected static object Db(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    protected static string? Text(DbDataReader reader, int index) => reader.IsDBNull(index) ? null : Convert.ToString(reader.GetValue(index));
    protected static bool Bool(DbDataReader reader, int index) => !reader.IsDBNull(index) && Convert.ToInt64(reader.GetValue(index)) == 1;
    protected static string? Date(string? value) => DateOnly.TryParse(value, out var date) && date != new DateOnly(1900, 1, 1) ? date.ToString("yyyy-MM-dd") : null;

    protected static byte[] ExpectedVersion(string? value, string kind, long id)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"RowVersion is required when changing a SQL Server {kind}.");
        try { return Convert.FromBase64String(value); }
        catch (FormatException) { throw new ArgumentException($"The {kind} RowVersion is not valid."); }
    }

    protected async Task AuditAsync(DbConnection connection, DbTransaction transaction, long caseId, string action, string entityType, long entityId, CancellationToken token)
    {
        await using var audit = connection.CreateCommand();
        audit.Transaction = transaction;
        audit.CommandText = "INSERT INTO dbo.audit_events (case_id,actor_user_id,action,entity_type,entity_id) VALUES (@caseId,@actor,@action,@type,@id)";
        audit.Parameters.Add(new SqlParameter("@caseId", caseId));
        audit.Parameters.Add(new SqlParameter("@actor", (object?)ActorUserId ?? DBNull.Value));
        audit.Parameters.Add(new SqlParameter("@action", action));
        audit.Parameters.Add(new SqlParameter("@type", entityType));
        audit.Parameters.Add(new SqlParameter("@id", entityId.ToString()));
        await audit.ExecuteNonQueryAsync(token);
    }

    protected static async Task EnsureCaseExistsAsync(DbConnection connection, DbTransaction transaction, long caseId, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT TOP (1) 1 FROM dbo.cases WHERE id=@caseId AND is_deleted=0";
        command.Parameters.Add(new SqlParameter("@caseId", caseId));
        if (await command.ExecuteScalarAsync(token) is null) throw new InvalidOperationException($"Case {caseId} does not exist in SQL Server.");
    }

    // Used to build a human-readable notification body ("Task 'X' assigned to you on 24-CV-100").
    // Runs on the same connection after the caller's own transaction has committed, since
    // notification creation is deliberately not coupled into the checklist item's transaction.
    protected static async Task<string?> GetCaseNumberAsync(DbConnection connection, long caseId, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT case_number FROM dbo.cases WHERE id=@caseId";
        command.Parameters.Add(new SqlParameter("@caseId", caseId));
        var result = await command.ExecuteScalarAsync(token);
        return result is null or DBNull ? null : Convert.ToString(result);
    }
}

public sealed class SqlServerDeadlineStore(IDatabaseConnectionFactory connections, IHttpContextAccessor accessor)
    : SqlServerWorkItemStoreBase(connections, accessor), IDeadlineStore
{
    public string Provider => "SqlServer";

    public async Task<List<DeadlineItem>> GetAsync(long? caseId, CancellationToken token = default)
    {
        var result = new List<DeadlineItem>();
        await using var connection = Connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,case_id,title,due_date,status,notes,source_type,is_manual,severity,completed_at,
                   source_kind,source_template_id,source_template_version,source_stage,generated_at,generated_by,row_version
            FROM dbo.deadlines
            WHERE is_deleted=0 AND (@caseId IS NULL OR case_id=@caseId)
            ORDER BY COALESCE(due_date,'9999-12-31'),title
            """;
        command.Parameters.Add(new SqlParameter("@caseId", (object?)caseId ?? DBNull.Value));
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token)) result.Add(Read(reader));
        }
        await AttachHistoryAsync(connection, result, caseId, token);
        return result;
    }

    public async Task<DeadlineItem> SaveAsync(DeadlineItem model, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(model.Title)) throw new ArgumentException("Deadline title is required.");
        var isNew = model.Id == 0;
        await using var connection = Connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        var now = DateTime.UtcNow.ToString("O");
        string? previousDue = null, previousStatus = null, previousCompleted = null;
        if (isNew)
        {
            await EnsureCaseExistsAsync(connection, transaction, model.CaseId, token);
        }
        else
        {
            await using var lookup = connection.CreateCommand();
            lookup.Transaction = transaction;
            lookup.CommandText = "SELECT due_date,status,completed_at,case_id FROM dbo.deadlines WHERE id=@id AND is_deleted=0";
            lookup.Parameters.Add(new SqlParameter("@id", model.Id));
            await using var reader = await lookup.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token)) { previousDue = Text(reader, 0); previousStatus = Text(reader, 1); previousCompleted = Text(reader, 2); model.CaseId = reader.GetInt64(3); }
        }
        var completed = model.Status is "Done" or "Complete" ? (previousStatus is "Done" or "Complete" ? previousCompleted : now) : null;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (isNew)
        {
            command.CommandText = """
                INSERT INTO dbo.deadlines (case_id,title,due_date,status,notes,source_type,is_manual,severity,created_at,updated_at,completed_at,
                    source_kind,source_template_id,source_template_version,source_stage,generated_at,generated_by)
                OUTPUT INSERTED.id,INSERTED.row_version
                VALUES (@caseId,@title,@due,@status,@notes,@sourceType,@manual,@severity,@now,@now,@completed,@sourceKind,@templateId,@templateVersion,@sourceStage,@generatedAt,@generatedBy)
                """;
        }
        else
        {
            command.CommandText = """
                UPDATE dbo.deadlines SET title=@title,due_date=@due,status=@status,notes=@notes,severity=@severity,updated_at=@now,completed_at=@completed
                OUTPUT INSERTED.id,INSERTED.row_version
                WHERE id=@id AND row_version=@version AND is_deleted=0
                """;
            command.Parameters.Add(new SqlParameter("@id", model.Id));
            command.Parameters.Add(new SqlParameter("@version", ExpectedVersion(model.RowVersion, "deadline", model.Id)));
        }
        AddParameters(command, model, completed, now);
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) throw new WorkItemConcurrencyException("Deadline", model.Id);
            model.Id = reader.GetInt64(0);
            model.RowVersion = Convert.ToBase64String((byte[])reader.GetValue(1));
        }
        model.CompletedAt = completed;
        if (!isNew && previousDue is not null && Date(previousDue) != Date(model.DueDate))
        {
            await using var history = connection.CreateCommand();
            history.Transaction = transaction;
            history.CommandText = "INSERT INTO dbo.deadline_history (deadline_id,previous_due_date,new_due_date,reason,created_at) VALUES (@id,@old,@new,@reason,@now)";
            history.Parameters.Add(new SqlParameter("@id", model.Id));
            history.Parameters.Add(new SqlParameter("@old", Db(Date(previousDue))));
            history.Parameters.Add(new SqlParameter("@new", Db(Date(model.DueDate))));
            history.Parameters.Add(new SqlParameter("@reason", Db(model.ReasonForChange)));
            history.Parameters.Add(new SqlParameter("@now", now));
            await history.ExecuteNonQueryAsync(token);
        }
        await AuditAsync(connection, transaction, model.CaseId, isNew ? "DeadlineCreated" : "DeadlineUpdated", "Deadline", model.Id, token);
        await transaction.CommitAsync(token);
        return model;
    }

    public async Task DeleteAsync(long id, string? rowVersion = null, CancellationToken token = default)
    {
        await using var connection = Connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE dbo.deadlines SET is_deleted=1,deleted_utc=SYSUTCDATETIME(),deleted_by_user_id=@actor OUTPUT INSERTED.case_id WHERE id=@id AND row_version=@version AND is_deleted=0";
        command.Parameters.Add(new SqlParameter("@actor", (object?)ActorUserId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@id", id));
        command.Parameters.Add(new SqlParameter("@version", ExpectedVersion(rowVersion, "deadline", id)));
        var caseId = await command.ExecuteScalarAsync(token);
        if (caseId is null) throw new WorkItemConcurrencyException("Deadline", id);
        await AuditAsync(connection, transaction, Convert.ToInt64(caseId), "DeadlineDeleted", "Deadline", id, token);
        await transaction.CommitAsync(token);
    }

    private static DeadlineItem Read(DbDataReader reader) => new()
    {
        Id=reader.GetInt64(0), CaseId=reader.GetInt64(1), Title=reader.GetString(2), DueDate=Date(Text(reader,3)),
        Status=Text(reader,4)??"Open", Notes=Text(reader,5), SourceType=Text(reader,6)??"Manual", IsManual=Bool(reader,7),
        Severity=Text(reader,8)??"normal", CompletedAt=Text(reader,9), SourceKind=Text(reader,10)??"Manual",
        SourceTemplateId=Text(reader,11), SourceTemplateVersion=reader.IsDBNull(12)?null:Convert.ToInt32(reader.GetValue(12)),
        SourceStage=Text(reader,13), GeneratedAt=Text(reader,14), GeneratedBy=Text(reader,15),
        RowVersion=Convert.ToBase64String((byte[])reader.GetValue(16))
    };

    private static void AddParameters(DbCommand command, DeadlineItem model, string? completed, string now)
    {
        command.Parameters.Add(new SqlParameter("@caseId",model.CaseId)); command.Parameters.Add(new SqlParameter("@title",model.Title.Trim()));
        command.Parameters.Add(new SqlParameter("@due",Db(Date(model.DueDate)))); command.Parameters.Add(new SqlParameter("@status",model.Status));
        command.Parameters.Add(new SqlParameter("@notes",Db(model.Notes))); command.Parameters.Add(new SqlParameter("@sourceType",model.SourceType));
        command.Parameters.Add(new SqlParameter("@manual",model.IsManual?1L:0L)); command.Parameters.Add(new SqlParameter("@severity",string.IsNullOrWhiteSpace(model.Severity)?"normal":model.Severity.Trim()));
        command.Parameters.Add(new SqlParameter("@now",now)); command.Parameters.Add(new SqlParameter("@completed",Db(completed)));
        command.Parameters.Add(new SqlParameter("@sourceKind",model.SourceKind)); command.Parameters.Add(new SqlParameter("@templateId",Db(model.SourceTemplateId)));
        command.Parameters.Add(new SqlParameter("@templateVersion",(object?)model.SourceTemplateVersion??DBNull.Value)); command.Parameters.Add(new SqlParameter("@sourceStage",Db(model.SourceStage)));
        command.Parameters.Add(new SqlParameter("@generatedAt",Db(model.GeneratedAt))); command.Parameters.Add(new SqlParameter("@generatedBy",Db(model.GeneratedBy)));
    }

    private static async Task AttachHistoryAsync(DbConnection connection, List<DeadlineItem> items, long? caseId, CancellationToken token)
    {
        if (items.Count==0) return;
        var byId=items.ToDictionary(x=>x.Id);
        await using var command=connection.CreateCommand();
        command.CommandText=caseId is null
            ? "SELECT deadline_id,previous_due_date,new_due_date,reason,created_at FROM dbo.deadline_history ORDER BY created_at"
            : "SELECT h.deadline_id,h.previous_due_date,h.new_due_date,h.reason,h.created_at FROM dbo.deadline_history h JOIN dbo.deadlines d ON d.id=h.deadline_id WHERE d.case_id=@caseId AND d.is_deleted=0 ORDER BY h.created_at";
        if(caseId is not null) command.Parameters.Add(new SqlParameter("@caseId",caseId));
        await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token)) if(byId.TryGetValue(reader.GetInt64(0),out var item)) item.History.Add(new(){PreviousDueDate=Text(reader,1),NewDueDate=Text(reader,2),Reason=Text(reader,3),ChangedAt=reader.GetString(4)});
    }
}

public sealed class SqlServerChecklistStore(IDatabaseConnectionFactory connections, IHttpContextAccessor accessor, SqlServerNotificationStore notifications, SqlServerCaseAssignmentRepository assignments)
    : SqlServerWorkItemStoreBase(connections, accessor), IChecklistStore
{
    public string Provider => "SqlServer";
    public async Task<List<ChecklistItemRecord>> GetAsync(long? caseId, CancellationToken token = default)
    {
        var result=new List<ChecklistItemRecord>(); await using var connection=Connections.CreateConnection(); await connection.OpenAsync(token);
        await using var command=connection.CreateCommand(); command.CommandText="""
            SELECT id,case_id,phase,task,due_date,status,notes,source_type,is_manual,completed_at,source_kind,source_template_id,source_template_version,source_stage,generated_at,generated_by,assigned_user_id,row_version
            FROM dbo.checklist_items WHERE is_deleted=0 AND (@caseId IS NULL OR case_id=@caseId) ORDER BY phase,task
            """; command.Parameters.Add(new SqlParameter("@caseId",(object?)caseId??DBNull.Value));
        await using var reader=await command.ExecuteReaderAsync(token); while(await reader.ReadAsync(token)) result.Add(Read(reader)); return result;
    }

    public async Task<ChecklistItemRecord> SaveAsync(ChecklistItemRecord model, CancellationToken token = default)
    {
        if(string.IsNullOrWhiteSpace(model.Task)) throw new ArgumentException("Checklist task is required."); var isNew=model.Id==0;
        await using var connection=Connections.CreateConnection(); await connection.OpenAsync(token); await using var transaction=await connection.BeginTransactionAsync(token);
        var now=DateTime.UtcNow.ToString("O"); string? previousStatus=null,previousCompleted=null,previousAssignedUserId=null;
        if(isNew) await EnsureCaseExistsAsync(connection,transaction,model.CaseId,token);
        else{await using var lookup=connection.CreateCommand();lookup.Transaction=transaction;lookup.CommandText="SELECT status,completed_at,case_id,assigned_user_id FROM dbo.checklist_items WHERE id=@id AND is_deleted=0";lookup.Parameters.Add(new SqlParameter("@id",model.Id));await using var reader=await lookup.ExecuteReaderAsync(token);if(await reader.ReadAsync(token)){previousStatus=Text(reader,0);previousCompleted=Text(reader,1);model.CaseId=reader.GetInt64(2);previousAssignedUserId=reader.IsDBNull(3)?null:reader.GetGuid(3).ToString();}}
        var isNowDone=model.Status is "Done" or "Complete"; var wasAlreadyDone=previousStatus is "Done" or "Complete";
        var completed=isNowDone?(wasAlreadyDone?previousCompleted:now):null;
        await using var command=connection.CreateCommand();command.Transaction=transaction;
        if(isNew) command.CommandText="""
            INSERT INTO dbo.checklist_items (case_id,phase,task,due_date,status,notes,source_type,is_manual,created_at,updated_at,completed_at,source_kind,source_template_id,source_template_version,source_stage,generated_at,generated_by,assigned_user_id)
            OUTPUT INSERTED.id,INSERTED.row_version VALUES (@caseId,@phase,@task,@due,@status,@notes,@sourceType,@manual,@now,@now,@completed,@sourceKind,@templateId,@templateVersion,@sourceStage,@generatedAt,@generatedBy,@assignedUserId)
            """;
        else{command.CommandText="""
            UPDATE dbo.checklist_items SET phase=@phase,task=@task,due_date=@due,status=@status,notes=@notes,updated_at=@now,completed_at=@completed,assigned_user_id=@assignedUserId
            OUTPUT INSERTED.id,INSERTED.row_version WHERE id=@id AND row_version=@version AND is_deleted=0
            """;command.Parameters.Add(new SqlParameter("@id",model.Id));command.Parameters.Add(new SqlParameter("@version",ExpectedVersion(model.RowVersion,"checklist item",model.Id)));}
        AddParameters(command,model,completed,now);await using(var reader=await command.ExecuteReaderAsync(token)){if(!await reader.ReadAsync(token))throw new WorkItemConcurrencyException("Checklist item",model.Id);model.Id=reader.GetInt64(0);model.RowVersion=Convert.ToBase64String((byte[])reader.GetValue(1));}
        model.CompletedAt=completed;await AuditAsync(connection,transaction,model.CaseId,isNew?"ChecklistItemCreated":"ChecklistItemUpdated","ChecklistItem",model.Id,token);await transaction.CommitAsync(token);

        // Notification triggers run after the checklist transaction commits - deliberately not
        // coupled into it, so a notification failure can never roll back the actual task save.
        var normalizedPreviousAssigned=string.IsNullOrWhiteSpace(previousAssignedUserId)?null:previousAssignedUserId;
        var normalizedNewAssigned=string.IsNullOrWhiteSpace(model.AssignedUserId)?null:model.AssignedUserId;
        if(normalizedNewAssigned is not null && !string.Equals(normalizedNewAssigned,normalizedPreviousAssigned,StringComparison.OrdinalIgnoreCase))
        {
            var caseNumber=await GetCaseNumberAsync(connection,model.CaseId,token);
            var body=string.IsNullOrWhiteSpace(caseNumber)?$"Task '{model.Task}' assigned to you.":$"Task '{model.Task}' assigned to you on {caseNumber}.";
            await notifications.CreateAsync([normalizedNewAssigned],"TaskAssigned",model.CaseId,"Task assigned",body,token);
        }
        if(isNowDone && !wasAlreadyDone)
        {
            var recipients=await assignments.GetCaseRoleUserIdsAsync(model.CaseId,"Attorney",token);
            if(recipients.Count>0)
            {
                var caseNumber=await GetCaseNumberAsync(connection,model.CaseId,token);
                var body=string.IsNullOrWhiteSpace(caseNumber)?$"Task '{model.Task}' completed.":$"Task '{model.Task}' completed on {caseNumber}.";
                await notifications.CreateAsync(recipients.Select(r=>r.ToString()).ToList(),"TaskCompleted",model.CaseId,"Task completed",body,token);
            }
        }

        return model;
    }

    public async Task DeleteAsync(long id,string? rowVersion=null,CancellationToken token=default)
    {
        await using var connection=Connections.CreateConnection();await connection.OpenAsync(token);await using var transaction=await connection.BeginTransactionAsync(token);await using var command=connection.CreateCommand();command.Transaction=transaction;
        command.CommandText="UPDATE dbo.checklist_items SET is_deleted=1,deleted_utc=SYSUTCDATETIME(),deleted_by_user_id=@actor OUTPUT INSERTED.case_id WHERE id=@id AND row_version=@version AND is_deleted=0";
        command.Parameters.Add(new SqlParameter("@actor",(object?)ActorUserId??DBNull.Value));command.Parameters.Add(new SqlParameter("@id",id));command.Parameters.Add(new SqlParameter("@version",ExpectedVersion(rowVersion,"checklist item",id)));
        var caseId=await command.ExecuteScalarAsync(token);if(caseId is null)throw new WorkItemConcurrencyException("Checklist item",id);await AuditAsync(connection,transaction,Convert.ToInt64(caseId),"ChecklistItemDeleted","ChecklistItem",id,token);await transaction.CommitAsync(token);
    }

    private static ChecklistItemRecord Read(DbDataReader r)=>new(){Id=r.GetInt64(0),CaseId=r.GetInt64(1),Phase=Text(r,2)??"",Task=r.GetString(3),DueDate=Date(Text(r,4)),Status=Text(r,5)??"Not Started",Notes=Text(r,6),SourceType=Text(r,7)??"Manual",IsManual=Bool(r,8),CompletedAt=Text(r,9),SourceKind=Text(r,10)??"Manual",SourceTemplateId=Text(r,11),SourceTemplateVersion=r.IsDBNull(12)?null:Convert.ToInt32(r.GetValue(12)),SourceStage=Text(r,13),GeneratedAt=Text(r,14),GeneratedBy=Text(r,15),AssignedUserId=r.IsDBNull(16)?null:r.GetGuid(16).ToString(),RowVersion=Convert.ToBase64String((byte[])r.GetValue(17))};
    // AssignedUserId is a GUID string (matches dbo.checklist_items.assigned_user_id uniqueidentifier,
    // FK'd to dbo.app_users(id)); only ever meaningfully populated once Entra/roster assignment is
    // in use, so an unparsable/blank value is stored as NULL rather than failing the save.
    private static void AddParameters(DbCommand c,ChecklistItemRecord m,string? completed,string now){c.Parameters.Add(new SqlParameter("@caseId",m.CaseId));c.Parameters.Add(new SqlParameter("@phase",m.Phase));c.Parameters.Add(new SqlParameter("@task",m.Task.Trim()));c.Parameters.Add(new SqlParameter("@due",Db(Date(m.DueDate))));c.Parameters.Add(new SqlParameter("@status",m.Status));c.Parameters.Add(new SqlParameter("@notes",Db(m.Notes)));c.Parameters.Add(new SqlParameter("@sourceType",m.SourceType));c.Parameters.Add(new SqlParameter("@manual",m.IsManual?1L:0L));c.Parameters.Add(new SqlParameter("@now",now));c.Parameters.Add(new SqlParameter("@completed",Db(completed)));c.Parameters.Add(new SqlParameter("@sourceKind",m.SourceKind));c.Parameters.Add(new SqlParameter("@templateId",Db(m.SourceTemplateId)));c.Parameters.Add(new SqlParameter("@templateVersion",(object?)m.SourceTemplateVersion??DBNull.Value));c.Parameters.Add(new SqlParameter("@sourceStage",Db(m.SourceStage)));c.Parameters.Add(new SqlParameter("@generatedAt",Db(m.GeneratedAt)));c.Parameters.Add(new SqlParameter("@generatedBy",Db(m.GeneratedBy)));c.Parameters.Add(new SqlParameter("@assignedUserId",Guid.TryParse(m.AssignedUserId,out var assignedGuid)?assignedGuid:DBNull.Value));}
}

// Multi-user rollout Phase 4a (notifications core), extended in Phase 4b (email delivery). The
// "shared insert" from the phase's requirements: CreateAsync takes plain recipient ids and text,
// with no knowledge of case_assignments or checklist_items - SqlServerChecklistStore and (as of 4b)
// DeadlineReminderBackgroundService are the current callers, each doing its own recipient resolution
// before calling in. Cannot be exercised against a live SQL Server in this sandbox - compile/review-
// only, like the rest of this project's SqlServer*-prefixed code.
public sealed class SqlServerNotificationStore(IDatabaseConnectionFactory connections, NotificationsOptions notificationsOptions, ILogger<SqlServerNotificationStore> logger) : INotificationStore
{
    public string Provider => "SqlServer";

    public async Task CreateAsync(IReadOnlyCollection<string> recipientUserIds, string notificationType, long? caseId, string title, string body, CancellationToken token = default)
    {
        var recipients = recipientUserIds
            .Select(r => Guid.TryParse(r, out var parsed) ? parsed : (Guid?)null)
            .Where(g => g is not null)
            .Select(g => g!.Value)
            .Distinct()
            .ToList();
        if (recipients.Count == 0) return;

        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        foreach (var recipient in recipients)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO dbo.notifications (recipient_user_id,case_id,notification_type,title,body) VALUES (@recipient,@caseId,@type,@title,@body)";
            command.Parameters.Add(new SqlParameter("@recipient", recipient));
            command.Parameters.Add(new SqlParameter("@caseId", (object?)caseId ?? DBNull.Value));
            command.Parameters.Add(new SqlParameter("@type", notificationType));
            command.Parameters.Add(new SqlParameter("@title", title));
            command.Parameters.Add(new SqlParameter("@body", (object?)body ?? DBNull.Value));
            await command.ExecuteNonQueryAsync(token);
        }
        await transaction.CommitAsync(token);

        // Email is best-effort and only ever attempted here, after the in-app rows are safely
        // committed - app_users.email is the only place a real recipient address can be resolved
        // from (SQLite has no such table at all, hence SqliteNotificationStore sends no email). A
        // failed/slow SMTP relay must never roll back or block the notification that's already saved.
        if (notificationsOptions.Enabled)
        {
            foreach (var recipient in recipients)
            {
                var (email, displayName) = await GetRecipientEmailAsync(connection, recipient, token);
                if (!string.IsNullOrWhiteSpace(email))
                {
                    await NotificationEmailSender.SendBestEffortAsync(notificationsOptions.Email, email, displayName, title, body, logger, token);
                }
            }
        }
    }

    private static async Task<(string? Email, string DisplayName)> GetRecipientEmailAsync(DbConnection connection, Guid recipient, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT email,display_name FROM dbo.app_users WHERE id=@id";
        command.Parameters.Add(new SqlParameter("@id", recipient));
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) return (null, "");
        return (reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetString(1));
    }

    public async Task<NotificationFeed> GetForRecipientAsync(string recipientUserId, int limit = 50, CancellationToken token = default)
    {
        var feed = new NotificationFeed();
        if (!Guid.TryParse(recipientUserId, out var recipient)) return feed;

        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(token);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT TOP (@limit) id,recipient_user_id,case_id,notification_type,title,body,is_read,created_at,read_at
                FROM dbo.notifications WHERE recipient_user_id=@recipient ORDER BY created_at DESC, id DESC
                """;
            command.Parameters.Add(new SqlParameter("@limit", limit));
            command.Parameters.Add(new SqlParameter("@recipient", recipient));
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                feed.Items.Add(new NotificationRecord
                {
                    Id = reader.GetInt64(0),
                    RecipientUserId = reader.GetGuid(1).ToString(),
                    CaseId = reader.IsDBNull(2) ? null : reader.GetInt64(2),
                    NotificationType = reader.GetString(3),
                    Title = reader.GetString(4),
                    Body = reader.IsDBNull(5) ? null : reader.GetString(5),
                    IsRead = reader.GetBoolean(6),
                    CreatedAt = reader.GetDateTime(7).ToString("O"),
                    ReadAt = reader.IsDBNull(8) ? null : reader.GetDateTime(8).ToString("O"),
                });
            }
        }

        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM dbo.notifications WHERE recipient_user_id=@recipient AND is_read=0";
        countCommand.Parameters.Add(new SqlParameter("@recipient", recipient));
        feed.UnreadCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(token));
        return feed;
    }

    public async Task<bool> MarkReadAsync(long id, string recipientUserId, CancellationToken token = default)
    {
        if (!Guid.TryParse(recipientUserId, out var recipient)) return false;
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE dbo.notifications SET is_read=1,read_at=SYSUTCDATETIME() WHERE id=@id AND recipient_user_id=@recipient";
        command.Parameters.Add(new SqlParameter("@id", id));
        command.Parameters.Add(new SqlParameter("@recipient", recipient));
        return await command.ExecuteNonQueryAsync(token) > 0;
    }

    public async Task MarkAllReadAsync(string recipientUserId, CancellationToken token = default)
    {
        if (!Guid.TryParse(recipientUserId, out var recipient)) return;
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE dbo.notifications SET is_read=1,read_at=SYSUTCDATETIME() WHERE recipient_user_id=@recipient AND is_read=0";
        command.Parameters.Add(new SqlParameter("@recipient", recipient));
        await command.ExecuteNonQueryAsync(token);
    }
}

public sealed class SqlServerDiscoveryTrackingStore(IDatabaseConnectionFactory connections, IHttpContextAccessor accessor)
    : SqlServerWorkItemStoreBase(connections, accessor), IDiscoveryTrackingStore
{
    public string Provider => "SqlServer";

    public async Task<List<DiscoveryItemRecord>> GetAsync(long? caseId, CancellationToken token = default)
    {
        var result = new List<DiscoveryItemRecord>();
        await using var connection = Connections.CreateConnection(); await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,case_id,request_title,direction,discovery_type,served_date,due_date,response_date,follow_up_date,status,
                   assigned_to,notes,escalation_note,good_faith_sent_date,motion_to_compel_date,row_version
            FROM dbo.discovery_tracking WHERE is_deleted=0 AND (@caseId IS NULL OR case_id=@caseId)
            ORDER BY COALESCE(due_date,'9999-12-31')
            """;
        command.Parameters.Add(new SqlParameter("@caseId", (object?)caseId ?? DBNull.Value));
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(Read(reader));
        return result;
    }

    public async Task<DiscoveryItemRecord> SaveAsync(DiscoveryItemRecord model, CancellationToken token = default)
    {
        var isNew = model.Id == 0;
        await using var connection = Connections.CreateConnection(); await connection.OpenAsync(token); await using var transaction = await connection.BeginTransactionAsync(token);
        if (isNew) await EnsureCaseExistsAsync(connection, transaction, model.CaseId, token);
        else
        {
            await using var lookup = connection.CreateCommand(); lookup.Transaction = transaction;
            lookup.CommandText = "SELECT case_id FROM dbo.discovery_tracking WHERE id=@id AND is_deleted=0"; lookup.Parameters.Add(new SqlParameter("@id", model.Id));
            var storedCase = await lookup.ExecuteScalarAsync(token); if (storedCase is not null) model.CaseId = Convert.ToInt64(storedCase);
        }
        var now = DateTime.UtcNow.ToString("O"); await using var command = connection.CreateCommand(); command.Transaction = transaction;
        if (isNew) command.CommandText = """
            INSERT INTO dbo.discovery_tracking (case_id,request_title,direction,discovery_type,served_date,due_date,response_date,follow_up_date,status,assigned_to,notes,escalation_note,good_faith_sent_date,motion_to_compel_date,created_at,updated_at)
            OUTPUT INSERTED.id,INSERTED.row_version VALUES (@caseId,@title,@direction,@type,@served,@due,@response,@followUp,@status,@assigned,@notes,@escalation,@goodFaith,@motion,@now,@now)
            """;
        else
        {
            command.CommandText = """
                UPDATE dbo.discovery_tracking SET request_title=@title,direction=@direction,discovery_type=@type,served_date=@served,due_date=@due,response_date=@response,
                    follow_up_date=@followUp,status=@status,assigned_to=@assigned,notes=@notes,escalation_note=@escalation,good_faith_sent_date=@goodFaith,motion_to_compel_date=@motion,updated_at=@now
                OUTPUT INSERTED.id,INSERTED.row_version WHERE id=@id AND row_version=@version AND is_deleted=0
                """;
            command.Parameters.Add(new SqlParameter("@id", model.Id)); command.Parameters.Add(new SqlParameter("@version", ExpectedVersion(model.RowVersion, "discovery item", model.Id)));
        }
        AddParameters(command, model, now);
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) throw new WorkItemConcurrencyException("Discovery item", model.Id);
            model.Id = reader.GetInt64(0); model.RowVersion = Convert.ToBase64String((byte[])reader.GetValue(1));
        }
        await AuditAsync(connection, transaction, model.CaseId, isNew ? "DiscoveryItemCreated" : "DiscoveryItemUpdated", "DiscoveryItem", model.Id, token);
        await transaction.CommitAsync(token); return model;
    }

    public async Task DeleteAsync(long id, string? rowVersion, CancellationToken token = default)
    {
        await using var connection = Connections.CreateConnection(); await connection.OpenAsync(token); await using var transaction = await connection.BeginTransactionAsync(token);
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "UPDATE dbo.discovery_tracking SET is_deleted=1,deleted_utc=SYSUTCDATETIME(),deleted_by_user_id=@actor OUTPUT INSERTED.case_id WHERE id=@id AND row_version=@version AND is_deleted=0";
        command.Parameters.Add(new SqlParameter("@actor", (object?)ActorUserId ?? DBNull.Value)); command.Parameters.Add(new SqlParameter("@id", id)); command.Parameters.Add(new SqlParameter("@version", ExpectedVersion(rowVersion, "discovery item", id)));
        var caseId = await command.ExecuteScalarAsync(token); if (caseId is null) throw new WorkItemConcurrencyException("Discovery item", id);
        await AuditAsync(connection, transaction, Convert.ToInt64(caseId), "DiscoveryItemDeleted", "DiscoveryItem", id, token); await transaction.CommitAsync(token);
    }

    private static DiscoveryItemRecord Read(DbDataReader r) => new()
    {
        Id=r.GetInt64(0),CaseId=r.GetInt64(1),RequestTitle=Text(r,2),Direction=Text(r,3)??"Served by Us",DiscoveryType=Text(r,4)??"Interrogatories",
        ServedDate=Date(Text(r,5)),DueDate=Date(Text(r,6)),ResponseDate=Date(Text(r,7)),FollowUpDate=Date(Text(r,8)),Status=Text(r,9)??"Waiting for Responses",
        AssignedTo=Text(r,10),Notes=Text(r,11),EscalationNote=Text(r,12),GoodFaithSentDate=Date(Text(r,13)),MotionToCompelDate=Date(Text(r,14)),RowVersion=Convert.ToBase64String((byte[])r.GetValue(15))
    };

    private static void AddParameters(DbCommand c, DiscoveryItemRecord m, string now)
    {
        c.Parameters.Add(new SqlParameter("@caseId",m.CaseId));c.Parameters.Add(new SqlParameter("@title",Db(m.RequestTitle)));c.Parameters.Add(new SqlParameter("@direction",m.Direction));c.Parameters.Add(new SqlParameter("@type",m.DiscoveryType));
        c.Parameters.Add(new SqlParameter("@served",Db(Date(m.ServedDate))));c.Parameters.Add(new SqlParameter("@due",Db(Date(m.DueDate))));c.Parameters.Add(new SqlParameter("@response",Db(Date(m.ResponseDate))));c.Parameters.Add(new SqlParameter("@followUp",Db(Date(m.FollowUpDate))));
        c.Parameters.Add(new SqlParameter("@status",m.Status));c.Parameters.Add(new SqlParameter("@assigned",Db(m.AssignedTo)));c.Parameters.Add(new SqlParameter("@notes",Db(m.Notes)));c.Parameters.Add(new SqlParameter("@escalation",Db(m.EscalationNote)));
        c.Parameters.Add(new SqlParameter("@goodFaith",Db(Date(m.GoodFaithSentDate))));c.Parameters.Add(new SqlParameter("@motion",Db(Date(m.MotionToCompelDate))));c.Parameters.Add(new SqlParameter("@now",now));
    }
}
