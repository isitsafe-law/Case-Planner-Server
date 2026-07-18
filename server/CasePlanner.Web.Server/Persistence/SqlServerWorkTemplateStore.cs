using System.Data.Common;
using CasePlanner.Data;
using CasePlanner.Web.Server.Models;
using Microsoft.Data.SqlClient;

namespace CasePlanner.Web.Server.Persistence;

public sealed class SqlServerWorkTemplateStore(IDatabaseConnectionFactory connections, IHttpContextAccessor accessor)
    : SqlServerWorkItemStoreBase(connections, accessor)
{
    public async Task<List<ChecklistTemplateRecord>> GetChecklistAsync(CancellationToken token = default)
    {
        var templates = new List<ChecklistTemplateRecord>();
        var byId = new Dictionary<long, ChecklistTemplateRecord>();
        await using var connection = Connections.CreateConnection();
        await connection.OpenAsync(token);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id,name,trigger_type,stage,issue_tag_name,track,active,row_version
                FROM dbo.checklist_templates WHERE is_deleted=0 ORDER BY name,id
                """;
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                var item = new ChecklistTemplateRecord
                {
                    Id = reader.GetInt64(0), Name = Text(reader, 1) ?? "", TriggerType = Text(reader, 2) ?? "Stage",
                    Stage = Text(reader, 3), IssueTagName = Text(reader, 4), Track = Text(reader, 5) ?? "Any",
                    Active = Bool(reader, 6), RowVersion = Version(reader, 7)
                };
                templates.Add(item);
                byId[item.Id] = item;
            }
        }
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id,template_id,task,phase,sort_order,due_offset_days,row_version
                FROM dbo.checklist_template_items WHERE is_deleted=0 ORDER BY template_id,sort_order,id
                """;
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                var templateId = reader.GetInt64(1);
                if (!byId.TryGetValue(templateId, out var template)) continue;
                template.Items.Add(new ChecklistTemplateItemRecord
                {
                    Id = reader.GetInt64(0), TemplateId = templateId, Task = Text(reader, 2) ?? "",
                    Phase = Text(reader, 3), SortOrder = reader.IsDBNull(4) ? 0 : Convert.ToInt32(reader.GetValue(4)),
                    DueOffsetDays = reader.IsDBNull(5) ? null : Convert.ToInt32(reader.GetValue(5)), RowVersion = Version(reader, 6)
                });
            }
        }
        return templates;
    }

    public async Task<ChecklistTemplateRecord> SaveChecklistAsync(ChecklistTemplateRecord model, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(model.Name)) throw new ArgumentException("Checklist template name is required.");
        var isNew = model.Id == 0;
        var now = DateTime.UtcNow.ToString("O");
        await using var connection = Connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (isNew)
        {
            command.CommandText = """
                INSERT INTO dbo.checklist_templates
                    (name,trigger_type,stage,issue_tag_name,case_type,track,active,created_at,updated_at,created_by_user_id,updated_by_user_id)
                OUTPUT INSERTED.id,INSERTED.row_version
                VALUES(@name,@trigger,@stage,@tag,'Any',@track,@active,@now,@now,@actor,@actor)
                """;
        }
        else
        {
            command.CommandText = """
                UPDATE dbo.checklist_templates SET name=@name,trigger_type=@trigger,stage=@stage,
                    issue_tag_name=@tag,track=@track,active=@active,updated_at=@now,updated_by_user_id=@actor
                OUTPUT INSERTED.id,INSERTED.row_version
                WHERE id=@id AND row_version=@version AND is_deleted=0
                """;
            command.Parameters.Add(new SqlParameter("@id", model.Id));
            command.Parameters.Add(new SqlParameter("@version", ExpectedVersion(model.RowVersion, "checklist template", model.Id)));
        }
        command.Parameters.Add(new SqlParameter("@name", model.Name.Trim()));
        command.Parameters.Add(new SqlParameter("@trigger", string.IsNullOrWhiteSpace(model.TriggerType) ? "Stage" : model.TriggerType.Trim()));
        command.Parameters.Add(new SqlParameter("@stage", Db(model.Stage)));
        command.Parameters.Add(new SqlParameter("@tag", Db(model.IssueTagName)));
        command.Parameters.Add(new SqlParameter("@track", string.IsNullOrWhiteSpace(model.Track) ? "Any" : model.Track.Trim()));
        command.Parameters.Add(new SqlParameter("@active", model.Active));
        command.Parameters.Add(new SqlParameter("@now", now));
        command.Parameters.Add(new SqlParameter("@actor", (object?)ActorUserId ?? DBNull.Value));
        await ReadIdentityAsync(command, model, "Checklist template", token);
        await GlobalAuditAsync(connection, transaction, isNew ? "ChecklistTemplateCreated" : "ChecklistTemplateUpdated", "ChecklistTemplate", model.Id, token);
        await transaction.CommitAsync(token);
        return model;
    }

    public async Task<ChecklistTemplateItemRecord> SaveChecklistItemAsync(ChecklistTemplateItemRecord model, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(model.Task)) throw new ArgumentException("Checklist template task is required.");
        var isNew = model.Id == 0;
        var now = DateTime.UtcNow.ToString("O");
        await using var connection = Connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        if (isNew && !await TemplateExistsAsync(connection, transaction, model.TemplateId, token))
            throw new InvalidOperationException($"Checklist template {model.TemplateId} does not exist in SQL Server.");
        if (!isNew) model.TemplateId = await ResolveTemplateIdAsync(connection, transaction, model.Id, model.TemplateId, token);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (isNew)
        {
            command.CommandText = """
                INSERT INTO dbo.checklist_template_items
                    (template_id,task,phase,sort_order,due_offset_days,created_at,updated_at,created_by_user_id,updated_by_user_id)
                OUTPUT INSERTED.id,INSERTED.row_version
                VALUES(@template,@task,@phase,@order,@offset,@now,@now,@actor,@actor)
                """;
        }
        else
        {
            command.CommandText = """
                UPDATE dbo.checklist_template_items SET task=@task,phase=@phase,sort_order=@order,
                    due_offset_days=@offset,updated_at=@now,updated_by_user_id=@actor
                OUTPUT INSERTED.id,INSERTED.row_version
                WHERE id=@id AND row_version=@version AND is_deleted=0
                """;
            command.Parameters.Add(new SqlParameter("@id", model.Id));
            command.Parameters.Add(new SqlParameter("@version", ExpectedVersion(model.RowVersion, "checklist template item", model.Id)));
        }
        command.Parameters.Add(new SqlParameter("@template", model.TemplateId));
        command.Parameters.Add(new SqlParameter("@task", model.Task.Trim()));
        command.Parameters.Add(new SqlParameter("@phase", Db(model.Phase)));
        command.Parameters.Add(new SqlParameter("@order", model.SortOrder));
        command.Parameters.Add(new SqlParameter("@offset", (object?)model.DueOffsetDays ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@now", now));
        command.Parameters.Add(new SqlParameter("@actor", (object?)ActorUserId ?? DBNull.Value));
        await ReadIdentityAsync(command, model, "Checklist template item", token);
        await GlobalAuditAsync(connection, transaction, isNew ? "ChecklistTemplateItemCreated" : "ChecklistTemplateItemUpdated", "ChecklistTemplateItem", model.Id, token);
        await transaction.CommitAsync(token);
        return model;
    }

    public async Task DeleteChecklistAsync(long id, string? rowVersion, CancellationToken token = default)
    {
        await using var connection = Connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using (var children = connection.CreateCommand())
        {
            children.Transaction = transaction;
            children.CommandText = "UPDATE dbo.checklist_template_items SET is_deleted=1,deleted_utc=SYSUTCDATETIME(),deleted_by_user_id=@actor WHERE template_id=@id AND is_deleted=0";
            children.Parameters.Add(new SqlParameter("@actor", (object?)ActorUserId ?? DBNull.Value));
            children.Parameters.Add(new SqlParameter("@id", id));
            await children.ExecuteNonQueryAsync(token);
        }
        await SoftDeleteGlobalAsync(connection, transaction, "checklist_templates", "Checklist template", "ChecklistTemplateDeleted", "ChecklistTemplate", id, rowVersion, token);
        await transaction.CommitAsync(token);
    }

    public Task DeleteChecklistItemAsync(long id, string? rowVersion, CancellationToken token = default) =>
        DeleteGlobalAsync("checklist_template_items", "Checklist template item", "ChecklistTemplateItemDeleted", "ChecklistTemplateItem", id, rowVersion, token);

    public async Task<List<DeadlineTemplateRecord>> GetDeadlinesAsync(CancellationToken token = default)
    {
        var result = new List<DeadlineTemplateRecord>();
        await using var connection = Connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,name,trigger_field,offset_days,title,severity,track,active,row_version
            FROM dbo.deadline_templates WHERE is_deleted=0 ORDER BY name,id
            """;
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            result.Add(new DeadlineTemplateRecord
            {
                Id = reader.GetInt64(0), Name = Text(reader, 1) ?? "", TriggerField = Text(reader, 2) ?? "filing_date",
                OffsetDays = Convert.ToInt32(reader.GetValue(3)), Title = Text(reader, 4) ?? "",
                Severity = Text(reader, 5) ?? "normal", Track = Text(reader, 6) ?? "Any",
                Active = Bool(reader, 7), RowVersion = Version(reader, 8)
            });
        return result;
    }

    public async Task<DeadlineTemplateRecord> SaveDeadlineAsync(DeadlineTemplateRecord model, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(model.Name) || string.IsNullOrWhiteSpace(model.Title))
            throw new ArgumentException("Deadline template name and title are required.");
        var isNew = model.Id == 0;
        var now = DateTime.UtcNow.ToString("O");
        await using var connection = Connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (isNew)
        {
            command.CommandText = """
                INSERT INTO dbo.deadline_templates
                    (name,trigger_field,offset_days,title,severity,case_type,track,active,created_at,updated_at,created_by_user_id,updated_by_user_id)
                OUTPUT INSERTED.id,INSERTED.row_version
                VALUES(@name,@trigger,@offset,@title,@severity,'Any',@track,@active,@now,@now,@actor,@actor)
                """;
        }
        else
        {
            command.CommandText = """
                UPDATE dbo.deadline_templates SET name=@name,trigger_field=@trigger,offset_days=@offset,
                    title=@title,severity=@severity,track=@track,active=@active,updated_at=@now,updated_by_user_id=@actor
                OUTPUT INSERTED.id,INSERTED.row_version
                WHERE id=@id AND row_version=@version AND is_deleted=0
                """;
            command.Parameters.Add(new SqlParameter("@id", model.Id));
            command.Parameters.Add(new SqlParameter("@version", ExpectedVersion(model.RowVersion, "deadline template", model.Id)));
        }
        command.Parameters.Add(new SqlParameter("@name", model.Name.Trim()));
        command.Parameters.Add(new SqlParameter("@trigger", string.IsNullOrWhiteSpace(model.TriggerField) ? "filing_date" : model.TriggerField.Trim()));
        command.Parameters.Add(new SqlParameter("@offset", model.OffsetDays));
        command.Parameters.Add(new SqlParameter("@title", model.Title.Trim()));
        command.Parameters.Add(new SqlParameter("@severity", string.IsNullOrWhiteSpace(model.Severity) ? "normal" : model.Severity.Trim()));
        command.Parameters.Add(new SqlParameter("@track", string.IsNullOrWhiteSpace(model.Track) ? "Any" : model.Track.Trim()));
        command.Parameters.Add(new SqlParameter("@active", model.Active));
        command.Parameters.Add(new SqlParameter("@now", now));
        command.Parameters.Add(new SqlParameter("@actor", (object?)ActorUserId ?? DBNull.Value));
        await ReadIdentityAsync(command, model, "Deadline template", token);
        await GlobalAuditAsync(connection, transaction, isNew ? "DeadlineTemplateCreated" : "DeadlineTemplateUpdated", "DeadlineTemplate", model.Id, token);
        await transaction.CommitAsync(token);
        return model;
    }

    public Task DeleteDeadlineAsync(long id, string? rowVersion, CancellationToken token = default) =>
        DeleteGlobalAsync("deadline_templates", "Deadline template", "DeadlineTemplateDeleted", "DeadlineTemplate", id, rowVersion, token);

    private async Task DeleteGlobalAsync(string table, string kind, string action, string entityType, long id, string? rowVersion, CancellationToken token)
    {
        await using var connection = Connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await SoftDeleteGlobalAsync(connection, transaction, table, kind, action, entityType, id, rowVersion, token);
        await transaction.CommitAsync(token);
    }

    private async Task SoftDeleteGlobalAsync(DbConnection connection, DbTransaction transaction, string table, string kind, string action, string entityType, long id, string? rowVersion, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"UPDATE dbo.{table} SET is_deleted=1,deleted_utc=SYSUTCDATETIME(),deleted_by_user_id=@actor OUTPUT INSERTED.id WHERE id=@id AND row_version=@version AND is_deleted=0";
        command.Parameters.Add(new SqlParameter("@actor", (object?)ActorUserId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@id", id));
        command.Parameters.Add(new SqlParameter("@version", ExpectedVersion(rowVersion, kind.ToLowerInvariant(), id)));
        if (await command.ExecuteScalarAsync(token) is null) throw new WorkItemConcurrencyException(kind, id);
        await GlobalAuditAsync(connection, transaction, action, entityType, id, token);
    }

    private async Task GlobalAuditAsync(DbConnection connection, DbTransaction transaction, string action, string entityType, long entityId, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO dbo.audit_events(case_id,actor_user_id,action,entity_type,entity_id) VALUES(NULL,@actor,@action,@type,@id)";
        command.Parameters.Add(new SqlParameter("@actor", (object?)ActorUserId ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@action", action));
        command.Parameters.Add(new SqlParameter("@type", entityType));
        command.Parameters.Add(new SqlParameter("@id", entityId.ToString()));
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task<bool> TemplateExistsAsync(DbConnection connection, DbTransaction transaction, long id, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT TOP(1) 1 FROM dbo.checklist_templates WHERE id=@id AND is_deleted=0";
        command.Parameters.Add(new SqlParameter("@id", id));
        return await command.ExecuteScalarAsync(token) is not null;
    }

    private static async Task<long> ResolveTemplateIdAsync(DbConnection connection, DbTransaction transaction, long id, long submitted, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT template_id FROM dbo.checklist_template_items WHERE id=@id AND is_deleted=0";
        command.Parameters.Add(new SqlParameter("@id", id));
        return await command.ExecuteScalarAsync(token) is { } value ? Convert.ToInt64(value) : submitted;
    }

    private static string Version(DbDataReader reader, int index) => Convert.ToBase64String((byte[])reader.GetValue(index));

    private static async Task ReadIdentityAsync(DbCommand command, ChecklistTemplateRecord model, string kind, CancellationToken token)
    {
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new WorkItemConcurrencyException(kind, model.Id);
        model.Id = reader.GetInt64(0); model.RowVersion = Version(reader, 1);
    }

    private static async Task ReadIdentityAsync(DbCommand command, ChecklistTemplateItemRecord model, string kind, CancellationToken token)
    {
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new WorkItemConcurrencyException(kind, model.Id);
        model.Id = reader.GetInt64(0); model.RowVersion = Version(reader, 1);
    }

    private static async Task ReadIdentityAsync(DbCommand command, DeadlineTemplateRecord model, string kind, CancellationToken token)
    {
        await using var reader = await command.ExecuteReaderAsync(token);
        if (!await reader.ReadAsync(token)) throw new WorkItemConcurrencyException(kind, model.Id);
        model.Id = reader.GetInt64(0); model.RowVersion = Version(reader, 1);
    }
}
