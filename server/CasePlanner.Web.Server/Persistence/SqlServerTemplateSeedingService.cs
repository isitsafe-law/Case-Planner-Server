using System.Data.Common;
using CasePlanner.Data;
using CasePlanner.Web.Server.Services;
using Microsoft.Data.SqlClient;

namespace CasePlanner.Web.Server.Persistence;

// SQL Server counterpart to CasePlannerRepository.SeedChecklistTemplatesAsync/
// SeedDeadlineTemplatesAsync. The SQLite side reseeds unconditionally on every
// CasePlannerRepository.InitializeAsync() call regardless of which provider is active - there is no
// SQL-Server-side seeding anywhere else, so on a genuine SQL Server deployment checklist_templates/
// deadline_templates would otherwise only ever get content from a one-time SqliteToSqlServerMigrator
// run and would silently go stale on every future content update. This service closes that gap, and
// is wired to run once at startup only when Database:ActiveProvider is SqlServer (see Program.cs).
//
// Shares the exact same seed content (CasePlannerRepository.TemplateSeeds/DeadlineTemplateSeeds) and
// the exact same version gate constants (ChecklistTemplateVersion/DeadlineTemplateVersion) as the
// SQLite path - a single source of truth for both "what the seed content is" and "what version it's
// currently at". The two tables' version gates stay independent, mirroring the SQLite code (checklist
// and deadline reseed separately, gated separately).
//
// Unlike the SQLite reseed (a hard DELETE - SQLite has no soft-delete concept anywhere in this app),
// this soft-deletes retired is_custom=0 rows (is_deleted=1, deleted_utc=SYSUTCDATETIME()), matching
// the convention every other SQL Server read/write against checklist_templates/
// checklist_template_items/deadline_templates already follows (all filter WHERE is_deleted=0 - see
// SqlServerWorkTemplateStore.cs). There is no FK anywhere from checklist_items/deadlines (the
// generated per-case rows) to checklist_template_items.id/deadline_templates.id -
// WorkflowGenerationService.cs keys generated rows off a computed SourceTemplateId string
// ("{template.Name}:{item.SortOrder}" for checklist tasks, template.Id.ToString() for deadlines,
// stored as plain text with no database-level foreign key constraint) - so a hard delete would not
// have orphaned anything either, but soft-delete keeps this table family internally consistent with
// its own established pattern.
public sealed class SqlServerTemplateSeedingService(IDatabaseConnectionFactory connections)
{
    public async Task SeedAsync(CancellationToken token = default)
    {
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(token);

        var checklistVersion = await GetAppSettingAsync(connection, "checklist_template_version", token);
        if (checklistVersion != CasePlannerRepository.ChecklistTemplateVersion)
        {
            await SeedChecklistTemplatesAsync(connection, token);
        }

        var deadlineVersion = await GetAppSettingAsync(connection, "deadline_template_version", token);
        if (deadlineVersion != CasePlannerRepository.DeadlineTemplateVersion)
        {
            await SeedDeadlineTemplatesAsync(connection, token);
        }
    }

    private static async Task SeedChecklistTemplatesAsync(DbConnection connection, CancellationToken token)
    {
        await using var transaction = await connection.BeginTransactionAsync(token);
        var now = DateTime.UtcNow.ToString("O");

        // is_custom-scoped soft-delete-and-reinsert (see CasePlannerRepository.
        // SeedChecklistTemplatesAsync for the SQLite analogue): is_custom=0 rows are stock seed
        // content and are always safe to retire/refresh. Any template or item a firm has touched
        // through the Template Editor has is_custom=1 on its parent template (SqlServerWorkTemplateStore
        // .SaveChecklistAsync/SaveChecklistItemAsync/DeleteChecklistItemAsync always set it) and is
        // never touched here, on any future version bump.
        await using (var retireItems = connection.CreateCommand())
        {
            retireItems.Transaction = transaction;
            retireItems.CommandText = """
                UPDATE dbo.checklist_template_items
                SET is_deleted=1, deleted_utc=SYSUTCDATETIME()
                WHERE is_deleted=0 AND template_id IN (
                    SELECT id FROM dbo.checklist_templates WHERE is_custom=0 AND is_deleted=0
                )
                """;
            await retireItems.ExecuteNonQueryAsync(token);
        }

        await using (var retireTemplates = connection.CreateCommand())
        {
            retireTemplates.Transaction = transaction;
            retireTemplates.CommandText = """
                UPDATE dbo.checklist_templates
                SET is_deleted=1, deleted_utc=SYSUTCDATETIME()
                WHERE is_custom=0 AND is_deleted=0
                """;
            await retireTemplates.ExecuteNonQueryAsync(token);
        }

        foreach (var seed in CasePlannerRepository.TemplateSeeds)
        {
            long templateId;
            await using (var insertTemplate = connection.CreateCommand())
            {
                insertTemplate.Transaction = transaction;
                insertTemplate.CommandText = """
                    INSERT INTO dbo.checklist_templates
                        (name, trigger_type, stage, issue_tag_name, case_type, active, is_custom, created_at, updated_at)
                    OUTPUT INSERTED.id
                    VALUES (@name, @trigger_type, @stage, @issue_tag_name, 'Any', 1, 0, @now, @now)
                    """;
                insertTemplate.Parameters.Add(new SqlParameter("@name", seed.Name));
                insertTemplate.Parameters.Add(new SqlParameter("@trigger_type", seed.TriggerType));
                insertTemplate.Parameters.Add(new SqlParameter("@stage", (object?)seed.Stage ?? DBNull.Value));
                insertTemplate.Parameters.Add(new SqlParameter("@issue_tag_name", (object?)seed.IssueTagName ?? DBNull.Value));
                insertTemplate.Parameters.Add(new SqlParameter("@now", now));
                templateId = Convert.ToInt64(await insertTemplate.ExecuteScalarAsync(token));
            }

            var phase = seed.TriggerType == "Stage" ? seed.Stage : seed.IssueTagName;
            for (var i = 0; i < seed.Tasks.Length; i++)
            {
                await using var insertItem = connection.CreateCommand();
                insertItem.Transaction = transaction;
                insertItem.CommandText = """
                    INSERT INTO dbo.checklist_template_items
                        (template_id, task, phase, sort_order, due_offset_days, created_at, updated_at)
                    VALUES (@template_id, @task, @phase, @sort_order, NULL, @now, @now)
                    """;
                insertItem.Parameters.Add(new SqlParameter("@template_id", templateId));
                insertItem.Parameters.Add(new SqlParameter("@task", seed.Tasks[i]));
                insertItem.Parameters.Add(new SqlParameter("@phase", (object?)phase ?? DBNull.Value));
                insertItem.Parameters.Add(new SqlParameter("@sort_order", i));
                insertItem.Parameters.Add(new SqlParameter("@now", now));
                await insertItem.ExecuteNonQueryAsync(token);
            }
        }

        await UpsertAppSettingAsync(connection, transaction, "checklist_template_version", CasePlannerRepository.ChecklistTemplateVersion, now, token);
        await transaction.CommitAsync(token);
    }

    private static async Task SeedDeadlineTemplatesAsync(DbConnection connection, CancellationToken token)
    {
        await using var transaction = await connection.BeginTransactionAsync(token);
        var now = DateTime.UtcNow.ToString("O");

        // is_custom-scoped soft-delete-and-reinsert (see SeedChecklistTemplatesAsync above).
        await using (var retire = connection.CreateCommand())
        {
            retire.Transaction = transaction;
            retire.CommandText = """
                UPDATE dbo.deadline_templates
                SET is_deleted=1, deleted_utc=SYSUTCDATETIME()
                WHERE is_custom=0 AND is_deleted=0
                """;
            await retire.ExecuteNonQueryAsync(token);
        }

        foreach (var seed in CasePlannerRepository.DeadlineTemplateSeeds)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO dbo.deadline_templates
                    (name, trigger_field, offset_days, title, severity, case_type, active, is_custom, created_at, updated_at)
                VALUES (@name, @trigger_field, @offset_days, @title, @severity, 'Any', 1, 0, @now, @now)
                """;
            insert.Parameters.Add(new SqlParameter("@name", seed.Name));
            insert.Parameters.Add(new SqlParameter("@trigger_field", seed.TriggerField));
            insert.Parameters.Add(new SqlParameter("@offset_days", seed.OffsetDays));
            insert.Parameters.Add(new SqlParameter("@title", seed.Title));
            insert.Parameters.Add(new SqlParameter("@severity", seed.Severity));
            insert.Parameters.Add(new SqlParameter("@now", now));
            await insert.ExecuteNonQueryAsync(token);
        }

        await UpsertAppSettingAsync(connection, transaction, "deadline_template_version", CasePlannerRepository.DeadlineTemplateVersion, now, token);
        await transaction.CommitAsync(token);
    }

    private static async Task<string?> GetAppSettingAsync(DbConnection connection, string key, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TOP(1) [value] FROM dbo.app_settings WHERE [key]=@key";
        command.Parameters.Add(new SqlParameter("@key", key));
        var result = await command.ExecuteScalarAsync(token);
        return result is null or DBNull ? null : Convert.ToString(result);
    }

    private static async Task UpsertAppSettingAsync(DbConnection connection, DbTransaction transaction, string key, string value, string now, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            IF EXISTS (SELECT 1 FROM dbo.app_settings WHERE [key]=@key)
                UPDATE dbo.app_settings SET [value]=@value, updated_at=@now WHERE [key]=@key;
            ELSE
                INSERT INTO dbo.app_settings ([key],[value],updated_at) VALUES (@key,@value,@now);
            """;
        command.Parameters.Add(new SqlParameter("@key", key));
        command.Parameters.Add(new SqlParameter("@value", value));
        command.Parameters.Add(new SqlParameter("@now", now));
        await command.ExecuteNonQueryAsync(token);
    }
}
