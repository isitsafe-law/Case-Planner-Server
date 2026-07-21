using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.VisualBasic.FileIO;
using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Persistence;
using CasePlanner.Web.Server.Security;

namespace CasePlanner.Web.Server.Services;

public sealed partial class CasePlannerRepository
{
    private readonly PathService _paths;
    private readonly IApplicationActorContext _actor;
    private readonly IDocumentStorage _documents;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public CasePlannerRepository(PathService paths,IApplicationActorContext? actor=null,IDocumentStorage? documents=null)
    {
        _paths = paths;
        _actor = actor??new LocalApplicationActorContext();
        _documents=documents??new FileSystemDocumentStorage(paths);
    }

    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = _paths.Config.DatabasePath,
        Mode = SqliteOpenMode.ReadWriteCreate
    }.ToString();

    public async Task InitializeAsync()
    {
        var databaseWasPresent = File.Exists(_paths.Config.DatabasePath);
        _paths.EnsureFolders();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, SchemaSql);
        await ExecuteAsync(connection, DocumentPlatformSchemaSql);
        // Build-plan step 7 follow-up: discovery_generations' only producer (the old Discovery
        // Content bulk editor) was fully retired, confirmed with zero real rows anywhere, and
        // nothing writes to it going forward - drop it from existing databases too rather than
        // just omitting it from SchemaSql, which would only stop it appearing in *new* ones.
        await ExecuteAsync(connection, "DROP TABLE IF EXISTS discovery_generations;");
        await EnsureSchemaUpgradesAsync(connection);
        await EnsureIssueTagCatalogAsync(connection);
        await EnsureDocumentPlatformSeedAsync(connection);
        await EnsureBuiltinDocumentTemplatesAsync(connection);
        await EnsureInterrogatoriesAllIssueTagSectionsAsync(connection);
        var templatesReseeded = await SeedChecklistTemplatesAsync(connection);
        var deadlineTemplatesReseeded = await SeedDeadlineTemplatesAsync(connection);
        await SeedAsync(connection, !databaseWasPresent);
        await EnsureImportSampleAsync();
        // Must run before the backfill below: it rekeys existing checklist_items to the
        // (now-current) stable template-name+sort_order source_type so the backfill's
        // already-generated check recognizes them and doesn't insert yet another duplicate.
        await DeduplicateChecklistItemsAsync(connection);
        if (templatesReseeded)
        {
            await BackfillChecklistsForCasesWithStageAsync(connection);
        }

        if (deadlineTemplatesReseeded)
        {
            await BackfillDeadlinesForCasesAsync(connection);
        }

        await BackfillServicePerfectedForAdvancedStagesAsync(connection);
        await CleanupOrphanedComputedDeadlinesAsync(connection);
        await CleanupRetired31DayServiceReminderAsync(connection);
        await MigrateOpposingCounselToAttorneysAsync(connection);
        await ApplyDeadlineClosureRulesRetroactivelyAsync(connection);
    }

    // One-time backfill: GenerateDeadlinesForCaseAsync now also closes a case's open computed
    // deadlines when the case is Closed, or closes filing-date-anchored service reminders when
    // the case's stage shows it already passed Service - not just when service_perfected is
    // explicitly set. Re-runs deadline generation for every case once so this new rule applies
    // to deadlines that were already computed under the old (narrower) closing rule.
    private async Task ApplyDeadlineClosureRulesRetroactivelyAsync(SqliteConnection connection)
    {
        const string flagKey = "deadline_closure_rules_v1";
        if (await GetAppSettingAsync(connection, flagKey) is not null)
        {
            return;
        }

        var ids = new List<long>();
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id FROM cases";
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                ids.Add(reader.GetInt64(0));
            }
        }

        var beforeCmd = connection.CreateCommand();
        beforeCmd.CommandText = "SELECT COUNT(*) FROM deadlines WHERE status NOT IN ('Done','Complete') AND severity = 'critical'";
        var before = Convert.ToInt32(await beforeCmd.ExecuteScalarAsync());

        await using (var tx = connection.BeginTransaction())
        {
            foreach (var id in ids)
            {
                await GenerateDeadlinesForCaseAsync(connection, tx, id);
            }

            await SetAppSettingAsync(connection, tx, flagKey, $"Applied stage/status-aware deadline closure to {ids.Count} case(s) at {DateTime.Now:G}");
            await tx.CommitAsync();
        }

        var afterCmd = connection.CreateCommand();
        afterCmd.CommandText = "SELECT COUNT(*) FROM deadlines WHERE status NOT IN ('Done','Complete') AND severity = 'critical'";
        var after = Convert.ToInt32(await afterCmd.ExecuteScalarAsync());

        await LogAsync($"Data-quality fix: applied stage/status-aware deadline closure across {ids.Count} case(s). Open critical deadlines: {before} -> {after}.");
    }

    // One-time cleanup: GenerateChecklistForCaseAsync used to key checklist_items.source_type off
    // checklist_template_items.id, but SeedChecklistTemplatesAsync wipes and reinserts every
    // template (fresh autoincrement ids) whenever ChecklistTemplateVersion bumps. That orphaned
    // every already-generated item's source_type, so the next reseed+backfill silently generated a
    // second copy of every task. The generator now keys off template name + sort_order instead
    // (stable across reseeds); this only cleans up duplicates that already exist from that bug.
    // Keeps the newest copy of each (case, task) pair and marks the older one N/A - never deletes,
    // and never touches manually-edited or completed items.
    private async Task DeduplicateChecklistItemsAsync(SqliteConnection connection)
    {
        const string flagKey = "checklist_item_dedup_v2";
        if (await GetAppSettingAsync(connection, flagKey) is not null)
        {
            return;
        }

        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id
            FROM checklist_items ci
            WHERE ci.source_type LIKE 'Template:%'
              AND ci.is_manual = 0
              AND ci.status = 'Not Started'
              AND ci.id NOT IN (
                  SELECT MAX(id) FROM checklist_items
                  WHERE source_type LIKE 'Template:%' AND is_manual = 0 AND status = 'Not Started'
                  GROUP BY case_id, task
              )
            """;
        var ids = new List<long>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                ids.Add(reader.GetInt64(0));
            }
        }

        // Any surviving row still keyed by the old numeric template-item id needs to be rekeyed to
        // the new stable "name:sortOrder" format if its task text still matches a current template
        // item - otherwise the next generation run won't recognize it as already-generated and will
        // insert yet another duplicate. Tasks that were removed from their template (like the
        // Discovery per-request tasks below) simply keep their old label; nothing will ever
        // regenerate them since no active template item matches that task text anymore.
        var rekeyCmd = connection.CreateCommand();
        rekeyCmd.CommandText = """
            SELECT ci.id, t.name, ti.sort_order
            FROM checklist_items ci
            JOIN checklist_template_items ti ON ti.task = ci.task
            JOIN checklist_templates t ON t.id = ti.template_id AND t.active = 1
            WHERE ci.source_type LIKE 'Template:%' AND ci.source_type NOT LIKE 'Template:%:%'
            """;
        var rekeys = new List<(long Id, string Key)>();
        await using (var reader = await rekeyCmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                rekeys.Add((reader.GetInt64(0), $"Template:{reader.GetString(1)}:{reader.GetInt32(2)}"));
            }
        }

        await using var tx = connection.BeginTransaction();
        var now = DateTime.UtcNow.ToString("O");
        foreach (var id in ids)
        {
            var update = connection.CreateCommand();
            update.Transaction = tx;
            update.CommandText = """
                UPDATE checklist_items
                SET status = 'N/A',
                    notes = CASE WHEN COALESCE(notes, '') = '' THEN @why ELSE notes || ' | ' || @why END,
                    updated_at = @now
                WHERE id = @id
                """;
            update.Parameters.AddWithValue("@id", id);
            update.Parameters.AddWithValue("@why", "Auto-marked N/A: duplicate generated by a prior template reseed.");
            update.Parameters.AddWithValue("@now", now);
            await update.ExecuteNonQueryAsync();
        }

        foreach (var (id, key) in rekeys)
        {
            var rekey = connection.CreateCommand();
            rekey.Transaction = tx;
            rekey.CommandText = "UPDATE checklist_items SET source_type = @key WHERE id = @id";
            rekey.Parameters.AddWithValue("@key", key);
            rekey.Parameters.AddWithValue("@id", id);
            await rekey.ExecuteNonQueryAsync();
        }

        await SetAppSettingAsync(connection, tx, flagKey, $"Marked {ids.Count} duplicate checklist item(s) N/A and rekeyed {rekeys.Count} at {DateTime.Now:G}");
        await tx.CommitAsync();

        if (ids.Count > 0 || rekeys.Count > 0)
        {
            await LogAsync($"Data-quality cleanup: marked {ids.Count} duplicate auto-generated checklist item(s) N/A and rekeyed {rekeys.Count} to the stable template dedup key.");
        }
    }

    // One-time cleanup: an earlier deadline-template reseed assigned new IDs to the standard
    // templates (old Computed:1-5 -> new Computed:6-10), but the deadline rows generated under
    // the old IDs were never removed. Those orphaned rows can never auto-close (the closure logic
    // in GenerateDeadlinesForCaseAsync only matches currently-active templates), so every case
    // carries permanent duplicate "Open" reminders including a duplicate critical 120-day service
    // deadline. Only deletes rows that are Open, not manually edited, and have no override history
    // in deadline_history - i.e. pure untouched auto-generated duplicates.
    private async Task CleanupOrphanedComputedDeadlinesAsync(SqliteConnection connection)
    {
        const string flagKey = "orphaned_computed_deadline_cleanup_v1";
        if (await GetAppSettingAsync(connection, flagKey) is not null)
        {
            return;
        }

        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT d.id
            FROM deadlines d
            WHERE d.source_type LIKE 'Computed:%'
              AND d.status = 'Open'
              AND d.is_manual = 0
              AND CAST(SUBSTR(d.source_type, 10) AS INTEGER) NOT IN (SELECT id FROM deadline_templates)
              AND NOT EXISTS (SELECT 1 FROM deadline_history dh WHERE dh.deadline_id = d.id)
            """;
        var ids = new List<long>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                ids.Add(reader.GetInt64(0));
            }
        }

        await using var tx = connection.BeginTransaction();
        foreach (var id in ids)
        {
            var delete = connection.CreateCommand();
            delete.Transaction = tx;
            delete.CommandText = "DELETE FROM deadlines WHERE id = @id";
            delete.Parameters.AddWithValue("@id", id);
            await delete.ExecuteNonQueryAsync();
        }

        await SetAppSettingAsync(connection, tx, flagKey, $"Deleted {ids.Count} orphaned duplicate computed deadline(s) at {DateTime.Now:G}");
        await tx.CommitAsync();

        if (ids.Count > 0)
        {
            await LogAsync($"Data-quality cleanup: deleted {ids.Count} orphaned duplicate auto-generated deadline row(s) left over from a prior template reseed.");
        }
    }

    // One-time cleanup: the "Service - 31 Day Status Reminder" deadline template and its matching
    // "Service status reminder: 31 days after filing" checklist task were retired - the reminder
    // fired on essentially every open case (flagging ~80% of the dashboard's attention list) and
    // added nothing the 60/90/120-day service milestones don't already cover. DeadlineTemplateVersion
    // and ChecklistTemplateVersion bumps stop new rows from being generated, but existing open rows
    // on already-generated cases need a one-time sweep. Completed rows are left alone so the
    // historical record (what was done and when) isn't erased.
    private async Task CleanupRetired31DayServiceReminderAsync(SqliteConnection connection)
    {
        const string flagKey = "retired_31_day_reminder_cleanup";
        if (await GetAppSettingAsync(connection, flagKey) is not null)
        {
            return;
        }

        const string deadlineTitle = "Service status reminder (31 days after filing)";
        const string checklistTask = "Service status reminder: 31 days after filing";

        var deadlineIds = new List<long>();
        var deadlineCmd = connection.CreateCommand();
        deadlineCmd.CommandText = """
            SELECT id FROM deadlines
            WHERE source_kind = 'DeadlineTemplate'
              AND title = @title
              AND status NOT IN ('Done', 'Complete')
            """;
        deadlineCmd.Parameters.AddWithValue("@title", deadlineTitle);
        await using (var reader = await deadlineCmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                deadlineIds.Add(reader.GetInt64(0));
            }
        }

        var checklistIds = new List<long>();
        var checklistCmd = connection.CreateCommand();
        checklistCmd.CommandText = """
            SELECT id FROM checklist_items
            WHERE source_kind = 'StageTemplate'
              AND task = @task
              AND status NOT IN ('Done', 'Complete', 'N/A')
            """;
        checklistCmd.Parameters.AddWithValue("@task", checklistTask);
        await using (var reader = await checklistCmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                checklistIds.Add(reader.GetInt64(0));
            }
        }

        await using var tx = connection.BeginTransaction();
        foreach (var id in deadlineIds)
        {
            // Clear history rows first, matching how deadlines are removed elsewhere (e.g. case
            // deletion) so no deadline_history row is left pointing at a deadline_id that no
            // longer exists.
            var deleteHistory = connection.CreateCommand();
            deleteHistory.Transaction = tx;
            deleteHistory.CommandText = "DELETE FROM deadline_history WHERE deadline_id = @id";
            deleteHistory.Parameters.AddWithValue("@id", id);
            await deleteHistory.ExecuteNonQueryAsync();

            var deleteDeadline = connection.CreateCommand();
            deleteDeadline.Transaction = tx;
            deleteDeadline.CommandText = "DELETE FROM deadlines WHERE id = @id";
            deleteDeadline.Parameters.AddWithValue("@id", id);
            await deleteDeadline.ExecuteNonQueryAsync();
        }

        foreach (var id in checklistIds)
        {
            var deleteChecklist = connection.CreateCommand();
            deleteChecklist.Transaction = tx;
            deleteChecklist.CommandText = "DELETE FROM checklist_items WHERE id = @id";
            deleteChecklist.Parameters.AddWithValue("@id", id);
            await deleteChecklist.ExecuteNonQueryAsync();
        }

        await SetAppSettingAsync(connection, tx, flagKey,
            $"Deleted {deadlineIds.Count} open 31-day service reminder deadline(s) and {checklistIds.Count} open matching checklist task(s) at {DateTime.Now:G}");
        await tx.CommitAsync();

        if (deadlineIds.Count > 0 || checklistIds.Count > 0)
        {
            await LogAsync($"Data-quality cleanup: retired the 31-day service status reminder - deleted {deadlineIds.Count} open deadline row(s) and {checklistIds.Count} open checklist task row(s). Completed rows were left in place.");
        }
    }

    // One-time migration (multi-user rollout Item 1): case.opposing_counsel is converted from a
    // single free-text field to the case_opposing_attorneys child table. The old column is kept
    // (not dropped) and the case editor stops writing to it going forward, but any case that
    // already has a non-blank opposing_counsel value needs that value preserved as a first row in
    // the new table so no existing data is silently lost. Only inserts when the case doesn't
    // already have any opposing-attorney rows, so re-runs (or a case that already migrated) never
    // duplicate the entry.
    private async Task MigrateOpposingCounselToAttorneysAsync(SqliteConnection connection)
    {
        const string flagKey = "opposing_counsel_migrated_v1";
        if (await GetAppSettingAsync(connection, flagKey) is not null)
        {
            return;
        }

        var candidates = new List<(long CaseId, string Name)>();
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, opposing_counsel FROM cases
            WHERE opposing_counsel IS NOT NULL AND TRIM(opposing_counsel) <> ''
              AND NOT EXISTS (SELECT 1 FROM case_opposing_attorneys WHERE case_id = cases.id)
            """;
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                candidates.Add((reader.GetInt64(0), reader.GetString(1).Trim()));
            }
        }

        await using var tx = connection.BeginTransaction();
        var now = DateTime.UtcNow.ToString("O");
        foreach (var (caseId, name) in candidates)
        {
            var insert = connection.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO case_opposing_attorneys (case_id, name, sort_order, created_at, updated_at)
                VALUES (@case_id, @name, 0, @now, @now)
                """;
            insert.Parameters.AddWithValue("@case_id", caseId);
            insert.Parameters.AddWithValue("@name", name);
            insert.Parameters.AddWithValue("@now", now);
            await insert.ExecuteNonQueryAsync();
        }

        await SetAppSettingAsync(connection, tx, flagKey, $"Migrated {candidates.Count} existing opposing_counsel value(s) into case_opposing_attorneys at {DateTime.Now:G}");
        await tx.CommitAsync();

        if (candidates.Count > 0)
        {
            await LogAsync($"Data migration: copied {candidates.Count} existing case(s)' opposing_counsel value into the new case_opposing_attorneys table.");
        }
    }

    // One-time data-quality fix: the original .xlsm import (M4) had no clean "Service Perfected"
    // column, so every real case imported with service_perfected=0, leaving every active case's
    // auto-generated 120-day service deadline permanently open and "critical" even for cases that
    // have clearly moved past the Service stage. Reaching Discovery or later is dispositive proof
    // service occurred, so infer service_perfected=true for those cases and let the existing
    // GenerateDeadlinesForCaseAsync close-on-perfection logic (see the "filing_date" branch above)
    // close the now-moot deadline. Gated to run once so it never overwrites a legitimate manual
    // "not yet served" flag a paralegal sets later on a case that regresses stage.
    private static readonly string[] StagesPastService =
    [
        "Discovery & Evaluation",
        "Trial Track",
        "Resolved"
    ];

    private async Task BackfillServicePerfectedForAdvancedStagesAsync(SqliteConnection connection)
    {
        const string flagKey = "service_perfected_backfill_v1";
        if (await GetAppSettingAsync(connection, flagKey) is not null)
        {
            return;
        }

        var ids = new List<long>();
        var placeholders = string.Join(", ", StagesPastService.Select((_, i) => $"@stage{i}"));
        var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT id FROM cases WHERE service_perfected = 0 AND stage IN ({placeholders})";
        for (var i = 0; i < StagesPastService.Length; i++)
        {
            cmd.Parameters.AddWithValue($"@stage{i}", StagesPastService[i]);
        }

        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                ids.Add(reader.GetInt64(0));
            }
        }

        await using var tx = connection.BeginTransaction();
        var now = DateTime.UtcNow.ToString("O");
        foreach (var id in ids)
        {
            var update = connection.CreateCommand();
            update.Transaction = tx;
            update.CommandText = "UPDATE cases SET service_perfected = 1, updated_at = @now WHERE id = @id";
            update.Parameters.AddWithValue("@now", now);
            update.Parameters.AddWithValue("@id", id);
            await update.ExecuteNonQueryAsync();

            await GenerateDeadlinesForCaseAsync(connection, tx, id);
        }

        await SetAppSettingAsync(connection, tx, flagKey, $"Backfilled service_perfected for {ids.Count} case(s) at {DateTime.Now:G}");
        await tx.CommitAsync();

        if (ids.Count > 0)
        {
            await LogAsync($"Data-quality backfill: inferred service_perfected=true for {ids.Count} case(s) already past the Service stage.");
        }
    }

    public async Task<DashboardData> GetDashboardAsync(IReadOnlySet<long>? allowedCaseIds = null)
    {
        bool Allowed(long caseId) => allowedCaseIds is null || allowedCaseIds.Contains(caseId);
        var cases = (await GetCasesAsync("", "", "", "", true)).Where(c => Allowed(c.Id)).ToList();
        var deadlines = (await GetDeadlinesAsync(null)).Where(x => Allowed(x.CaseId)).ToList();
        var checklist = (await GetChecklistItemsAsync(null)).Where(x => Allowed(x.CaseId)).ToList();
        var discovery = (await GetDiscoveryItemsAsync(null)).Where(x => Allowed(x.CaseId)).ToList();
        var publicationEntries = (await GetPublicationRecordsAsync(null)).Where(x => Allowed(x.CaseId)).ToList();
        var hearings = (await GetHearingsAsync(null)).Where(x => Allowed(x.CaseId)).ToList();
        var serviceSummaries = BuildServiceSummaries(cases, publicationEntries);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var agenda = new List<AttentionItem>();
        var upcoming = new List<AttentionItem>();

        void AddItem(List<AttentionItem> bucket, string kind, long caseId, long? itemId, string summary, string? dueDate, string targetTab)
        {
            var caseRecord = cases.FirstOrDefault(c => c.Id == caseId);
            if (caseRecord is null)
            {
                return;
            }

            bucket.Add(new AttentionItem
            {
                Kind = kind,
                CaseId = caseId,
                ItemId = itemId,
                CaseName = caseRecord.CaseName,
                CaseNumber = caseRecord.CaseNumber,
                Summary = summary,
                DueDate = dueDate,
                TargetTab = targetTab
            });
        }

        var agendaStaleCutoff = today.AddDays(-CaseAttentionEngine.UnconfirmedAfterDays);
        foreach (var deadline in deadlines.Where(d => d.Status is not "Done" and not "Complete"))
        {
            var due = ParseDate(deadline.DueDate);
            if (due is null)
            {
                continue;
            }

            if (due <= today)
            {
                // A deadline this stale is presumptively part of the same unconfirmed-historical
                // backlog CaseAttentionEngine already downgrades at the case level (see
                // UnconfirmedAfterDays) - it isn't "today's" business by any reading, so it's left
                // out of the cross-case agenda entirely rather than burying real items under decades
                // of auto-generated reminders. The raw deadline is still visible on the case's own
                // Deadlines tab for anyone who wants that level of detail.
                if (due < agendaStaleCutoff)
                {
                    continue;
                }

                AddItem(agenda, "deadline", deadline.CaseId, deadline.Id, deadline.Title, deadline.DueDate, "deadlines");
            }
            else if (due <= today.AddDays(30))
            {
                AddItem(upcoming, "deadline", deadline.CaseId, deadline.Id, deadline.Title, deadline.DueDate, "deadlines");
            }
        }

        foreach (var item in checklist.Where(i => i.Status is not "Done" and not "Complete" and not "N/A"))
        {
            var due = ParseDate(item.DueDate);
            if (due is null)
            {
                continue;
            }

            if (due <= today)
            {
                AddItem(agenda, "checklist", item.CaseId, item.Id, item.Task, item.DueDate, "checklist");
            }
            else if (due <= today.AddDays(30))
            {
                AddItem(upcoming, "checklist", item.CaseId, item.Id, item.Task, item.DueDate, "checklist");
            }
        }

        foreach (var item in discovery.Where(d => d.Status.Contains("Follow-Up", StringComparison.OrdinalIgnoreCase) || d.Status.Contains("Waiting", StringComparison.OrdinalIgnoreCase)))
        {
            var dueDate = item.FollowUpDate ?? item.DueDate;
            var due = ParseDate(dueDate);
            var bucket = due is not null && due > today ? upcoming : agenda;
            AddItem(bucket, "discovery", item.CaseId, item.Id, $"{item.Direction} {item.DiscoveryType}", dueDate, "discovery");
        }

        // Only "missing" has no equivalent row here: a case with no filing_date never gets a
        // computed 120-day deadline row at all (GenerateDeadlinesForCaseAsync has no anchor date
        // to work with), so this is the only way to surface it. "overdue"/"urgent"/"upcoming" are
        // deliberately not mirrored into the Agenda - they describe the exact same 120-day
        // deadline that's already present as a "deadline" kind row sourced from the deadlines
        // table, and showing both was producing two near-identical rows for one real fact.
        foreach (var service in serviceSummaries.Where(item => item.WarningLevel == "missing"))
        {
            AddItem(agenda, "service", service.CaseId, null, service.WarningText, service.ServiceDeadline120, "details");
        }

        // Note: cases with no next action set are surfaced as a single rolled-up count
        // (CasesNeedingReview below), not as one placeholder Agenda row per case - a case
        // missing a next action isn't a dated, actionable item, and there's no per-case content
        // to show beyond the same repeated "not set" text.

        foreach (var caseRecord in cases.Where(c => ParseDate(c.TrialDate) is { } trial && trial > today && trial <= today.AddDays(120)))
        {
            AddItem(upcoming, "trial", caseRecord.Id, null, "Trial / hearing date", caseRecord.TrialDate, "overview");
        }

        var activeCases = cases.Where(c => c.Status is not "Closed" and not "Complete" and not "Triage").ToList();
        // "unconfirmed" is deliberately excluded here - it's a distinct, lower-stakes backlog
        // (service records with no data to confirm or deny them), not part of the genuinely
        // urgent/attention/stalled worklist this table is meant to be a prioritized view of.
        var attentionCases = activeCases
            .Where(c => c.AttentionStatus is "urgent" or "attention" or "stalled")
            .OrderBy(c => c.AttentionStatus switch { "urgent" => 0, "attention" => 1, _ => 2 })
            .ThenBy(c => ParseDate(c.NextDeadlineDate) ?? DateOnly.MaxValue)
            .Take(15)
            .ToList();

        var deadlinesByCase = deadlines.GroupBy(d => d.CaseId).ToDictionary(g => g.Key, g => (IReadOnlyList<DeadlineItem>)g.ToList());
        var checklistByCase = checklist.GroupBy(i => i.CaseId).ToDictionary(g => g.Key, g => (IReadOnlyList<ChecklistItemRecord>)g.ToList());
        var discoveryByCase = discovery.GroupBy(d => d.CaseId).ToDictionary(g => g.Key, g => (IReadOnlyList<DiscoveryItemRecord>)g.ToList());
        var hearingsByCase = hearings.GroupBy(h => h.CaseId).ToDictionary(g => g.Key, g => (IReadOnlyList<HearingRecord>)g.ToList());
        var postureByCase = (await GetDiscoveryPosturesAsync(null)).Where(x => Allowed(x.CaseId)).ToDictionary(p => p.CaseId);
        var serviceByCase = serviceSummaries.ToDictionary(s => s.CaseId);
        var emptyDeadlines = (IReadOnlyList<DeadlineItem>)[];
        var emptyChecklist = (IReadOnlyList<ChecklistItemRecord>)[];
        var emptyDiscovery = (IReadOnlyList<DiscoveryItemRecord>)[];
        var emptyHearings = (IReadOnlyList<HearingRecord>)[];

        var triageQueue = activeCases
            .Select(c => DashboardTriageEngine.Evaluate(
                c,
                deadlinesByCase.GetValueOrDefault(c.Id, emptyDeadlines),
                checklistByCase.GetValueOrDefault(c.Id, emptyChecklist),
                discoveryByCase.GetValueOrDefault(c.Id, emptyDiscovery),
                serviceByCase.GetValueOrDefault(c.Id),
                hearingsByCase.GetValueOrDefault(c.Id, emptyHearings),
                today,
                postureByCase.GetValueOrDefault(c.Id)?.IsComplete == true))
            .Where(entry => entry is not null)
            .Select(entry => entry!)
            .OrderBy(entry => entry.PriorityScore)
            .ThenBy(entry => ParseDate(entry.DueDate) ?? DateOnly.MaxValue)
            .ToList();

        return new DashboardData
        {
            OverdueDeadlines = deadlines.Count(d => d.Status is not "Done" and not "Complete" && ParseDate(d.DueDate) is { } due && due < today),
            DueIn7Days = deadlines.Count(d => d.Status is not "Done" and not "Complete" && ParseDate(d.DueDate) is { } due && due >= today && due <= today.AddDays(7)),
            DueIn30Days = deadlines.Count(d => d.Status is not "Done" and not "Complete" && ParseDate(d.DueDate) is { } due && due >= today && due <= today.AddDays(30)),
            UpcomingTrials = cases.Count(c => ParseDate(c.TrialDate) is { } trial && trial >= today && trial <= today.AddDays(120)),
            DiscoveryDue = discovery.Count(d => d.Status.Contains("Waiting", StringComparison.OrdinalIgnoreCase) && ParseDate(d.DueDate) is { } due && due <= today.AddDays(7)),
            DiscoveryFollowUps = discovery.Count(d => d.Status.Contains("Follow-Up", StringComparison.OrdinalIgnoreCase)),
            ChecklistDueSoon = checklist.Count(i => i.Status is not "Done" and not "Complete" and not "N/A" && ParseDate(i.DueDate) is { } due && due <= today.AddDays(7)),
            ServiceDueSoon = serviceSummaries.Count(s => s.WarningLevel is "urgent" or "upcoming"),
            ServiceOverdue = serviceSummaries.Count(s => s.WarningLevel == "overdue"),
            CasesWithoutPerfectedService = serviceSummaries.Count(s => s.ServiceRequired && !s.ServicePerfected),
            MissingServiceDeadline = serviceSummaries.Count(s => s.WarningLevel == "missing"),
            CasesNeedingReview = cases.Count(c => string.IsNullOrWhiteSpace(c.NextAction)),
            ActiveCaseCount = activeCases.Count,
            CasesUrgentCount = activeCases.Count(c => c.AttentionStatus == "urgent"),
            CasesAttentionCount = activeCases.Count(c => c.AttentionStatus == "attention"),
            CasesUnconfirmedCount = activeCases.Count(c => c.AttentionStatus == "unconfirmed"),
            CasesStalledCount = activeCases.Count(c => c.AttentionStatus == "stalled"),
            CasesOnTrackCount = activeCases.Count(c => c.AttentionStatus == "onTrack"),
            AttentionCases = attentionCases,
            // Undated items (e.g. a discovery item awaiting a response with no set follow-up date)
            // sort after every dated item instead of jumping to the front - a missing date isn't a
            // signal of higher priority, and letting it act like one was pushing genuinely dated,
            // actionable items further down the list.
            TodaysAgenda = agenda.OrderBy(a => ParseDate(a.DueDate) ?? DateOnly.MaxValue).Take(20).ToList(),
            UpcomingDates = upcoming.OrderBy(a => ParseDate(a.DueDate) ?? today.AddDays(365)).Take(20).ToList(),
            TriageQueue = triageQueue,
            // Distinct-case counts per category (a case can match more than one - e.g. both
            // Service Risk and Stale Review - so these don't sum to triageQueue.Count).
            NeedsActionNowCount = triageQueue.Count(e => e.MatchedCategories.Contains("needsActionNow")),
            ServiceRiskCount = triageQueue.Count(e => e.MatchedCategories.Contains("serviceRisk")),
            HardDeadlinesSoonCount = triageQueue.Count(e => e.MatchedCategories.Contains("hardDeadlinesSoon")),
            CourtEventsSoonCount = triageQueue.Count(e => e.MatchedCategories.Contains("courtEventsSoon")),
            BlockedCount = triageQueue.Count(e => e.MatchedCategories.Contains("blocked")),
            StaleReviewCount = triageQueue.Count(e => e.MatchedCategories.Contains("staleReview"))
        };
    }

    private sealed record EvaluatedMatter(CaseRecord Case, bool IsPreFiling, int? DaysSince, string? Momentum, DiscoveryPosture? Posture);

    // The Attorney Dashboard aggregation - one bulk read of cases/deadlines/hearings/discovery
    // postures, grouped into dictionaries, then evaluated in memory per case (same N+1-avoidance
    // shape GetDashboardAsync already uses). SummaryCounts is always computed over the full active
    // docket so the top filter cards don't shift as content filters are applied; every other
    // section respects `filters`.
    public async Task<AttorneyDashboardResponse> GetAttorneyDashboardAsync(AttorneyDashboardFilters filters, IReadOnlySet<long>? allowedCaseIds = null)
    {
        {
            bool IsAllowed(long caseId)=>allowedCaseIds is null||allowedCaseIds.Contains(caseId);
            var dashboardCases=(await GetCasesAsync("","","","",true)).Where(c=>IsAllowed(c.Id)).ToList();
            var dashboardDeadlines=(await GetDeadlinesAsync(null)).Where(x=>IsAllowed(x.CaseId)).ToList();
            var dashboardHearings=(await GetHearingsAsync(null)).Where(x=>IsAllowed(x.CaseId)).ToList();
            var dashboardPostures=(await GetDiscoveryPosturesAsync(null)).Where(x=>IsAllowed(x.CaseId)).ToList();
            return AttorneyDashboardComposer.Compose(dashboardCases,dashboardDeadlines,dashboardHearings,dashboardPostures,filters);
        }
#pragma warning disable CS0162
        bool Allowed(long caseId) => allowedCaseIds is null || allowedCaseIds.Contains(caseId);
        var allCases = (await GetCasesAsync("", "", "", "", true)).Where(c => Allowed(c.Id)).ToList();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var activeCases = allCases.Where(c => c.Status is not "Closed" and not "Complete" and not "Triage").ToList();
        var triageCaseCount = allCases.Count(c => c.Status == "Triage");

        var deadlines = (await GetDeadlinesAsync(null)).Where(x => Allowed(x.CaseId)).ToList();
        var hearings = (await GetHearingsAsync(null)).Where(x => Allowed(x.CaseId)).ToList();
        var postures = (await GetDiscoveryPosturesAsync(null)).Where(x => Allowed(x.CaseId)).ToList();

        var openDeadlinesByCase = deadlines.Where(d => d.Status is not ("Done" or "Complete"))
            .GroupBy(d => d.CaseId).ToDictionary(g => g.Key, g => (IReadOnlyList<DeadlineItem>)g.ToList());
        var hearingsByCase = hearings.GroupBy(h => h.CaseId).ToDictionary(g => g.Key, g => (IReadOnlyList<HearingRecord>)g.ToList());
        var postureByCase = postures.ToDictionary(p => p.CaseId);
        var emptyDeadlines = (IReadOnlyList<DeadlineItem>)Array.Empty<DeadlineItem>();
        var emptyHearings = (IReadOnlyList<HearingRecord>)Array.Empty<HearingRecord>();

        var evaluated = activeCases.Select(c =>
        {
            // Consolidated Case Status is the source of truth. MatterType remains a legacy
            // compatibility fallback for older imported records that have not been projected yet.
            var isPreFiling = string.Equals(c.CaseStatus, "Pipeline", StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.MatterType, "PreFilingTract", StringComparison.OrdinalIgnoreCase);
            var daysSince = AttorneyDashboardEngine.DaysSinceMeaningfulActivity(c, today);
            var momentum = isPreFiling ? null : AttorneyDashboardEngine.EvaluateMomentumStatus(c, today, daysSince);
            var posture = isPreFiling ? null : postureByCase.GetValueOrDefault(c.Id);
            return new EvaluatedMatter(c, isPreFiling, daysSince, momentum, posture);
        }).ToList();

        bool MatchesFilters(EvaluatedMatter m)
        {
            var c = m.Case;
            if (!string.IsNullOrWhiteSpace(filters.MatterType) && !string.Equals(c.MatterType, filters.MatterType, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrWhiteSpace(filters.Project) && !string.Equals(c.ProjectName, filters.Project, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrWhiteSpace(filters.County) && !string.Equals(c.County, filters.County, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrWhiteSpace(filters.Priority) && !string.Equals(c.Priority, filters.Priority, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrWhiteSpace(filters.CurrentHolder) && !string.Equals(c.CurrentHolder, filters.CurrentHolder, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrWhiteSpace(filters.Stage) && !string.Equals(c.Stage, filters.Stage, StringComparison.OrdinalIgnoreCase) && !string.Equals(c.PipelineStage, filters.Stage, StringComparison.OrdinalIgnoreCase)) return false;
            if (filters.TrialTrack is { } tt && c.TrialTrack != tt) return false;
            if (!string.IsNullOrWhiteSpace(filters.MomentumStatus) && !string.Equals(m.Momentum, filters.MomentumStatus, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrWhiteSpace(filters.Search))
            {
                var s = filters.Search;
                var hit = c.CaseName.Contains(s, StringComparison.OrdinalIgnoreCase)
                    || c.CaseNumber.Contains(s, StringComparison.OrdinalIgnoreCase)
                    || c.JobNumber.Contains(s, StringComparison.OrdinalIgnoreCase);
                if (!hit) return false;
            }

            return true;
        }

        // --- Summary cards: always the full active docket, unaffected by content filters ---
        var summaryCounts = new AttorneyDashboardSummaryCounts
        {
            NeedsJudgment = evaluated.Count(m => !m.IsPreFiling
                    ? AttorneyDashboardEngine.EvaluateFiledCase(m.Case, m.Posture, openDeadlinesByCase.GetValueOrDefault(m.Case.Id, emptyDeadlines), hearingsByCase.GetValueOrDefault(m.Case.Id, emptyHearings), today)?.ActionCategory == "Decide"
                    : string.Equals(m.Case.CurrentHolder, "Attorney", StringComparison.OrdinalIgnoreCase)),
            Stalled = evaluated.Count(m => m.Momentum == "Stalled"),
            DiscoveryUnset = evaluated.Count(m => !m.IsPreFiling && AttorneyDashboardEngine.EvaluateDiscoveryConditions(m.Posture, today).Contains("Strategy not selected")),
            OnMyDesk = evaluated.Count(m => string.Equals(m.Case.CurrentHolder, "Attorney", StringComparison.OrdinalIgnoreCase)),
            TrialTrack = evaluated.Count(m => m.Case.TrialTrack),
            MissingNextReview = evaluated.Count(m => !m.IsPreFiling
                && string.IsNullOrWhiteSpace(m.Case.WaitingOn)
                && (string.IsNullOrWhiteSpace(m.Case.NextAction) || string.IsNullOrWhiteSpace(m.Case.NextReviewDate ?? m.Case.NextActionDue))),
        };

        var matched = evaluated.Where(MatchesFilters).ToList();

        var actionQueue = new List<ActionQueueItem>();
        var momentumReview = new List<MomentumReviewEntry>();
        var discoveryControl = new DiscoveryControlSummary();
        var myDesk = new List<PreFilingTractRow>();
        var waitingRows = new List<PreFilingTractRow>();
        var allPipeline = new List<PreFilingTractRow>();
        var trialWatch = new List<TrialWatchEntry>();

        foreach (var m in matched)
        {
            var c = m.Case;
            if (m.IsPreFiling)
            {
                var bucket = AttorneyDashboardEngine.PipelineBucket(c);
                var monitorReason = bucket == "Waiting" ? AttorneyDashboardEngine.WaitingMonitorReason(c, today, m.DaysSince) : AttorneyDashboardEngine.MyDeskFlagReason(c);
                var row = new PreFilingTractRow
                {
                    CaseId = c.Id,
                    TractOrOwnerName = string.IsNullOrWhiteSpace(c.CaseName) ? c.Landowner ?? c.Tract : c.CaseName,
                    ProjectName = c.ProjectName,
                    JobNumber = c.JobNumber,
                    County = c.County,
                    CurrentHolder = c.CurrentHolder,
                    PipelineStage = c.PipelineStage,
                    DateSentToCurrentHolder = c.DateSentToCurrentHolder,
                    Priority = c.Priority,
                    NextReviewDate = c.NextReviewDate,
                    CurrentIssue = c.CurrentIssue,
                    LastFollowUpDate = c.WaitingFollowUpDate,
                    LastUpdated = c.UpdatedAt,
                    FlagReason = monitorReason,
                };
                allPipeline.Add(row);
                if (bucket == "MyDesk") myDesk.Add(row); else waitingRows.Add(row);

                if (AttorneyDashboardEngine.PreFilingBelongsInActionQueue(c, today))
                {
                    actionQueue.Add(AttorneyDashboardEngine.EvaluatePreFilingTract(c, today));
                }

                continue;
            }

            var item = AttorneyDashboardEngine.EvaluateFiledCase(c, m.Posture, openDeadlinesByCase.GetValueOrDefault(c.Id, emptyDeadlines), hearingsByCase.GetValueOrDefault(c.Id, emptyHearings), today);
            if (item is not null)
            {
                actionQueue.Add(item);
            }

            momentumReview.Add(new MomentumReviewEntry
            {
                CaseId = c.Id,
                CaseName = c.CaseName,
                CaseNumber = string.IsNullOrWhiteSpace(c.CaseNumber) ? null : c.CaseNumber,
                MomentumStatus = m.Momentum ?? "Moving",
                DaysSinceMeaningfulActivity = m.DaysSince ?? 0,
                WaitingOn = c.WaitingOn,
                WaitingFollowUpDate = c.WaitingFollowUpDate,
            });

            foreach (var condition in AttorneyDashboardEngine.EvaluateDiscoveryConditions(m.Posture, today))
            {
                IncrementDiscoveryCondition(discoveryControl, condition, c, m.Posture);
            }

            if (AttorneyDashboardEngine.IsTrialWatchEligible(c, today, AttorneyDashboardEngine.DefaultTrialWatchDays))
            {
                trialWatch.Add(BuildTrialWatchEntry(c, m.Posture, today));
            }
        }

        actionQueue = actionQueue
            .OrderBy(a => a.PriorityLevel)
            .ThenBy(a => ParseDate(a.ReviewDate) ?? DateOnly.MaxValue)
            .ThenByDescending(a => a.DaysSinceMeaningfulActivity ?? 0)
            .ToList();

        var upcomingDecisions = actionQueue
            .Where(a => a.ActionCategory == "Decide")
            .Select(a => new UpcomingDecisionItem
            {
                CaseId = a.CaseId,
                CaseName = a.CaseName,
                DecisionType = a.Reason,
                RelevantDate = a.ReviewDate,
                Context = a.PostureSummary,
                RecommendedPreparationDate = a.ReviewDate,
                Status = "Pending",
            })
            .ToList();

        var projectWatch = BuildProjectWatch(matched.Select(m => m.Case), today);

        var docketSummary = new AttorneyDocketSummary
        {
            PreFilingMatters = matched.Count(m => m.IsPreFiling),
            FiledMatters = matched.Count(m => !m.IsPreFiling),
            TrialTrackMatters = matched.Count(m => m.Case.TrialTrack),
            WaitingAppropriately = matched.Count(m => m.Momentum == "Waiting Appropriately"),
            OnAttorneysDesk = matched.Count(m => string.Equals(m.Case.CurrentHolder, "Attorney", StringComparison.OrdinalIgnoreCase)),
            MissingNextReviewDate = matched.Count(m => !m.IsPreFiling
                && string.IsNullOrWhiteSpace(m.Case.WaitingOn)
                && (string.IsNullOrWhiteSpace(m.Case.NextAction) || string.IsNullOrWhiteSpace(m.Case.NextReviewDate ?? m.Case.NextActionDue))),
        };

        return new AttorneyDashboardResponse
        {
            SummaryCounts = summaryCounts,
            ActionQueue = actionQueue,
            DiscoveryControl = discoveryControl,
            MomentumReview = momentumReview,
            FilingPipeline = new FilingPipelineView { MyDesk = myDesk, Waiting = waitingRows, AllPipeline = allPipeline },
            TrialWatch = trialWatch,
            UpcomingDecisions = upcomingDecisions,
            ProjectWatch = projectWatch,
            DocketSummary = docketSummary,
            TriageCaseCount = triageCaseCount,
        };
#pragma warning restore CS0162
    }

    private static void IncrementDiscoveryCondition(DiscoveryControlSummary summary, string condition, CaseRecord c, DiscoveryPosture? posture)
    {
        switch (condition)
        {
            case "Strategy not selected": summary.StrategyNotSelected++; break;
            case "Strategy selected but discovery not served": summary.StrategySelectedNotServed++; break;
            case "Responses overdue": summary.ResponsesOverdue++; break;
            case "Responses received but not reviewed": summary.ResponsesReceivedNotReviewed++; break;
            case "Deficiencies unresolved": summary.DeficienciesUnresolved++; break;
            case "Deposition decision pending": summary.DepositionDecisionPending++; break;
            case "Discovery cutoff approaching": summary.CutoffApproaching++; break;
            case "Discovery complete": summary.Complete++; break;
            case "No discovery currently needed": summary.NoDiscoveryNeeded++; break;
        }

        if (!summary.CasesByCondition.TryGetValue(condition, out var list))
        {
            list = [];
            summary.CasesByCondition[condition] = list;
        }

        list.Add(new DiscoveryControlCaseRef
        {
            CaseId = c.Id,
            CaseName = c.CaseName,
            CaseNumber = string.IsNullOrWhiteSpace(c.CaseNumber) ? null : c.CaseNumber,
            Strategy = posture?.Strategy ?? "Strategy not selected",
            NextDecision = posture?.NextDecision,
            NextReviewDate = posture?.NextReviewDate,
        });
    }

    private static TrialWatchEntry BuildTrialWatchEntry(CaseRecord c, DiscoveryPosture? posture, DateOnly today)
    {
        int? daysUntilTrial = DateOnly.TryParse(c.TrialDate, out var trial) ? trial.DayNumber - today.DayNumber : null;
        var discoveryStatus = posture is null
            ? "Strategy not selected"
            : posture.IsComplete ? "Discovery complete" : posture.Strategy;
        return new TrialWatchEntry
        {
            CaseId = c.Id,
            CaseName = c.CaseName,
            CaseNumber = string.IsNullOrWhiteSpace(c.CaseNumber) ? null : c.CaseNumber,
            TrialDate = c.TrialDate,
            DaysUntilTrial = daysUntilTrial,
            Deposit = c.DepositAmount,
            OwnerAppraisal = null,
            OwnerDemand = null,
            LastOffer = null,
            SettlementAuthority = null,
            FeeComparisonNote = AttorneyDashboardEngine.BuildFeeComparisonNote(c.DepositAmount, null, null),
            DiscoveryStatus = discoveryStatus,
            WitnessReadiness = null,
            ExhibitReadiness = null,
            NextTrialDecision = string.IsNullOrWhiteSpace(c.NextAction) ? "Confirm final valuation position and settlement recommendation" : c.NextAction,
        };
    }

    // Shared-issue detection is intentionally conservative: it only fires on a concrete signal
    // actually present in the data model (2+ tracts in the same project sharing an appraiser,
    // with 2+ of them stalled) rather than inferring "repeated valuation theory" or "common access
    // claims," which nothing in the schema captures today. See the technical note for this gap.
    private static List<ProjectWatchRow> BuildProjectWatch(IEnumerable<CaseRecord> cases, DateOnly today)
    {
        var rows = new List<ProjectWatchRow>();
        var groups = cases
            .Where(c => !string.IsNullOrWhiteSpace(c.ProjectName) || !string.IsNullOrWhiteSpace(c.JobNumber))
            .GroupBy(c => string.IsNullOrWhiteSpace(c.ProjectName) ? c.JobNumber! : c.ProjectName!);

        foreach (var group in groups)
        {
            var tracts = group.ToList();
            if (tracts.Count < 2)
            {
                continue;
            }

            var stalledTracts = tracts.Where(c => AttorneyDashboardEngine.EvaluateMomentumStatus(c, today, AttorneyDashboardEngine.DaysSinceMeaningfulActivity(c, today)) == "Stalled").ToList();
            var appraiserGroups = tracts.Where(c => !string.IsNullOrWhiteSpace(c.Appraiser))
                .GroupBy(c => c.Appraiser!)
                .Where(g => g.Count() >= 2 && g.Count(c => stalledTracts.Contains(c)) >= 2)
                .ToList();
            var sharedIssue = appraiserGroups.Count > 0
                ? $"Possible common appraiser delay: {appraiserGroups[0].Key} across {appraiserGroups[0].Count()} tracts"
                : null;

            var earliestTrial = tracts.Where(c => DateOnly.TryParse(c.TrialDate, out _)).Select(c => DateOnly.Parse(c.TrialDate!)).OrderBy(d => d).FirstOrDefault();
            var oldestInactive = tracts
                .Select(c => (c.CaseName, Days: AttorneyDashboardEngine.DaysSinceMeaningfulActivity(c, today)))
                .Where(x => x.Days is not null)
                .OrderByDescending(x => x.Days)
                .FirstOrDefault();

            rows.Add(new ProjectWatchRow
            {
                ProjectName = group.Key,
                JobNumber = tracts.Select(c => c.JobNumber).FirstOrDefault(j => !string.IsNullOrWhiteSpace(j)),
                TractCount = tracts.Count,
                PreFilingCount = tracts.Count(c => c.MatterType == "PreFilingTract"),
                FiledCount = tracts.Count(c => c.MatterType != "PreFilingTract"),
                ResolvedCount = tracts.Count(c => c.Status is "Closed" or "Complete"),
                OnAttorneyDeskCount = tracts.Count(c => string.Equals(c.CurrentHolder, "Attorney", StringComparison.OrdinalIgnoreCase)),
                StalledCount = stalledTracts.Count,
                EarliestTrialDate = earliestTrial == default ? null : earliestTrial.ToString("yyyy-MM-dd"),
                OldestInactiveMatter = oldestInactive.CaseName,
                SharedIssue = sharedIssue,
                NextProjectDecision = sharedIssue is null ? null : "Review whether the shared appraiser delay warrants a coordinated response across tracts",
            });
        }

        return rows;
    }

    public async Task<List<CaseRecord>> GetCasesAsync(string search, string status, string county, string stage, bool includeClosed, string track = "", string caseStatus = "", string dateOpenedFrom = "", string dateOpenedTo = "", string dateClosedFrom = "", string dateClosedTo = "")
    {
        var list = new List<CaseRecord>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, case_number, case_name, job_number, tract, county, status,
                   filing_date, date_of_taking, trial_date, next_action, next_action_due,
                   deposit_amount, owner, landowner, valuation_notes, settlement_notes,
                   publication_service_notes, service_required, service_perfected, service_perfected_date,
                   service_deadline_120, service_deadline_basis_date, service_method, service_notes,
                   service_status, created_at, updated_at, stage, track,
                   assigned_attorney, opposing_counsel, appraiser, taxes_owed,
                   funds_withdrawn, funds_withdrawn_date, discovery_completed, updated_appraisal, closed_date,
                   project_name, tax_owed_amount, whole_property_acres, acquisition_acres,
                   landowner_appraiser_name, additional_deposit_amount, additional_deposit_date,
                   matter_type, priority, current_holder, pipeline_stage, date_sent_to_current_holder,
                   next_review_date, last_meaningful_activity_date, momentum_status, waiting_reason,
                   waiting_on, waiting_started_date, expected_response, waiting_follow_up_date,
                   waiting_escalation_action, trial_track, short_posture_summary, current_issue,
                   deferred_until, deferred_reason, deferred_at, deferred_by,
                   (SELECT COUNT(*) FROM checklist_items ci WHERE ci.case_id = cases.id) AS checklist_total,
                   (SELECT COUNT(*) FROM checklist_items ci WHERE ci.case_id = cases.id AND ci.status IN ('Done', 'Complete', 'N/A')) AS checklist_done,
                   COALESCE(case_status, 'Pipeline') AS case_status,
                   COALESCE(status_mapping_review, 0) AS status_mapping_review,
                   date_opened, trial_end_date, property_description
            FROM cases
            WHERE (@includeClosed = 1 OR COALESCE(status,'') NOT IN ('Closed','Complete'))
              AND (@search = '' OR case_number LIKE @like OR case_name LIKE @like OR job_number LIKE @like OR tract LIKE @like)
              AND (@status = '' OR status = @status)
              AND (@county = '' OR county = @county)
              AND (@stage = '' OR stage = @stage)
              AND (@track = '' OR track = @track)
              AND (@caseStatus = '' OR COALESCE(case_status, 'Pipeline') = @caseStatus)
              AND (@dateOpenedFrom = '' OR date_opened >= @dateOpenedFrom)
              AND (@dateOpenedTo = '' OR date_opened <= @dateOpenedTo)
              AND (@dateClosedFrom = '' OR closed_date >= @dateClosedFrom)
              AND (@dateClosedTo = '' OR closed_date <= @dateClosedTo)
            ORDER BY case_name
            """;
        cmd.Parameters.AddWithValue("@includeClosed", includeClosed ? 1 : 0);
        cmd.Parameters.AddWithValue("@search", search);
        cmd.Parameters.AddWithValue("@like", $"%{search}%");
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@county", county);
        cmd.Parameters.AddWithValue("@stage", stage);
        cmd.Parameters.AddWithValue("@track", track);
        cmd.Parameters.AddWithValue("@caseStatus", caseStatus);
        cmd.Parameters.AddWithValue("@dateOpenedFrom", dateOpenedFrom);
        cmd.Parameters.AddWithValue("@dateOpenedTo", dateOpenedTo);
        cmd.Parameters.AddWithValue("@dateClosedFrom", dateClosedFrom);
        cmd.Parameters.AddWithValue("@dateClosedTo", dateClosedTo);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(ReadCase(reader));
        }

        if (list.Count > 0)
        {
            await ApplyCaseAttentionAsync(list);
        }

        return list;
    }

    private async Task ApplyCaseAttentionAsync(List<CaseRecord> cases)
    {
        var deadlines = await GetDeadlinesAsync(null);
        var deadlinesByCase = deadlines.GroupBy(d => d.CaseId).ToDictionary(g => g.Key, g => (IReadOnlyList<DeadlineItem>)g.ToList());

        var lastActivityByCase = new Dictionary<long, string>();
        await using (var connection = new SqliteConnection(ConnectionString))
        {
            await connection.OpenAsync();
            var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT case_id, MAX(updated_at) FROM (
                    SELECT case_id, updated_at FROM checklist_items
                    UNION ALL
                    SELECT case_id, updated_at FROM deadlines
                    UNION ALL
                    SELECT case_id, updated_at FROM discovery_tracking
                )
                GROUP BY case_id
                """;
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (!reader.IsDBNull(1))
                {
                    lastActivityByCase[reader.GetInt64(0)] = reader.GetString(1);
                }
            }
        }

        foreach (var c in cases)
        {
            deadlinesByCase.TryGetValue(c.Id, out var caseDeadlines);
            caseDeadlines ??= [];
            lastActivityByCase.TryGetValue(c.Id, out var childActivity);

            string? lastActivity = string.IsNullOrEmpty(c.UpdatedAt) ? null : c.UpdatedAt;
            if (!string.IsNullOrEmpty(childActivity) && (lastActivity is null || string.CompareOrdinal(childActivity, lastActivity) > 0))
            {
                lastActivity = childActivity;
            }

            var (status, nextDate, nextTitle) = CaseAttentionEngine.Compute(caseDeadlines, lastActivity, c.Status);
            c.AttentionStatus = status;
            c.NextDeadlineDate = nextDate;
            c.NextDeadlineTitle = nextTitle;
            c.LastActivityAt = lastActivity;
        }
    }

    public async Task<CaseWorkspaceResponse?> GetCaseWorkspaceAsync(long caseId, IReadOnlySet<long>? dashboardCaseIds = null)
    {
        var cases = await GetCasesAsync("", "", "", "", true);
        var found = cases.FirstOrDefault(c => c.Id == caseId);
        if (found is null)
        {
            return null;
        }

        var publicationEntries = await GetPublicationEntriesAsync(caseId);
        var publication = await GetPublicationRecordAsync(caseId) ?? new PublicationRecord { CaseId = caseId };

        return new CaseWorkspaceResponse
        {
            Case = found,
            Deadlines = await GetDeadlinesAsync(caseId),
            ChecklistItems = await GetChecklistItemsAsync(caseId),
            DiscoveryItems = await GetDiscoveryItemsAsync(caseId),
            PublicationEntries = publicationEntries,
            Publication = publication,
            ServiceLogEntries = await GetServiceLogEntriesAsync(caseId),
            OpposingAttorneys = await GetOpposingAttorneysAsync(caseId),
            AvailableIssueTags = await GetIssueTagsAsync(),
            CaseIssueTags = await GetCaseIssueTagsAsync(caseId),
            CaseNotes = await GetCaseNotesAsync(caseId),
            Hearings = await GetHearingsAsync(caseId),
            DocumentExports = await GetDocumentExportsAsync(caseId),
            ServiceStatus = ServiceStatusEngine.Build(found, publication),
            OverviewSummary = await GetDashboardAsync(dashboardCaseIds)
        };
    }

    public async Task<List<ServiceQueueItem>> GetServiceQueueAsync(IReadOnlySet<long>? allowedCaseIds = null)
    {
        bool Allowed(long caseId) => allowedCaseIds is null || allowedCaseIds.Contains(caseId);
        var cases = (await GetCasesAsync("", "", "", "", true)).Where(c => Allowed(c.Id)).ToList();
        var publicationEntries = (await GetPublicationRecordsAsync(null)).Where(x => Allowed(x.CaseId)).ToList();
        return BuildServiceSummaries(cases, publicationEntries)
            .OrderBy(item => ParseDate(item.ServiceDeadline120) ?? DateOnly.MaxValue)
            .ThenBy(item => item.CaseName)
            .ToList();
    }

    public async Task<List<UpcomingWorkItemRecord>> GetUpcomingWorkAsync(string? type, string? urgency, int limit, IReadOnlySet<long>? allowedCaseIds = null)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        bool Allowed(long caseId) => allowedCaseIds is null || allowedCaseIds.Contains(caseId);
        var cases = (await GetCasesAsync("", "", "", "", true)).Where(c => Allowed(c.Id)).ToList();
        var eligibleCases = cases.Where(IsOpenCase).ToDictionary(c => c.Id);
        var deadlines = await GetDeadlinesAsync(null);
        var checklist = await GetChecklistItemsAsync(null);
        var discovery = await GetDiscoveryItemsAsync(null);
        var service = await GetServiceQueueAsync(allowedCaseIds);
        var hearings = await GetHearingsAsync(null);
        var rows = new List<UpcomingWorkItemRecord>();

        bool Eligible(long caseId, string itemType)
        {
            if (!eligibleCases.TryGetValue(caseId, out var record)) return false;
            if (record.DeferredUntil is not null && DateOnly.TryParse(record.DeferredUntil, out var deferred) && deferred > today) return false;
            return record.CaseStatus != "Pipeline" || itemType == "service";
        }

        void Add(string key, long caseId, string title, string itemType, string? dueDate, string tab)
        {
            if (!Eligible(caseId, itemType) || (type is not null && type != "all" && type != itemType)) return;
            DateOnly? due = DateOnly.TryParse(dueDate, out var parsed) ? parsed : null;
            var days = due.HasValue ? due.Value.DayNumber - today.DayNumber : (int?)null;
            var itemUrgency = !days.HasValue ? "No Due Date" : days < 0 ? "Overdue" : days == 0 ? "Due Today" : days <= 7 ? "Next 7 Days" : days <= 14 ? "Next 14 Days" : days <= 30 ? "Next 30 Days" : "Later";
            var requestedUrgency = urgency ?? "All Open";
            var matches = requestedUrgency == "All Open" || requestedUrgency == itemUrgency || requestedUrgency == "Next 7 Days" && days is >= 0 and <= 7 || requestedUrgency == "Next 14 Days" && days is >= 0 and <= 14 || requestedUrgency == "Next 30 Days" && days is >= 0 and <= 30;
            if (!matches) return;
            rows.Add(new UpcomingWorkItemRecord { Key = key, CaseId = caseId, CaseName = eligibleCases[caseId].CaseName, Title = title, Type = itemType, DueDate = dueDate, Urgency = itemUrgency, IsOverdue = days < 0, Tab = tab });
        }

        foreach (var item in checklist.Where(i => i.Status is not ("Done" or "Complete" or "N/A"))) Add($"task-{item.Id}", item.CaseId, item.Task, "task", item.DueDate, "checklist");
        foreach (var item in deadlines.Where(i => i.Status is not ("Done" or "Complete"))) Add($"deadline-{item.Id}", item.CaseId, item.Title, "deadline", item.DueDate, "deadlines");
        foreach (var item in discovery.Where(i => !i.Status.Contains("complete", StringComparison.OrdinalIgnoreCase) && !i.Status.Contains("cancel", StringComparison.OrdinalIgnoreCase))) Add($"discovery-{item.Id}", item.CaseId, item.RequestTitle ?? $"{item.Direction} {item.DiscoveryType}", "discovery", item.FollowUpDate ?? item.DueDate, "discovery");
        foreach (var item in service.Where(i => !i.ServicePerfected)) Add($"service-{item.CaseId}", item.CaseId, item.ServiceDeadline120 is null ? "Complete service record" : "Perfect service", "service", item.ServiceDeadline120 ?? item.FilingDate, "details");
        foreach (var item in hearings) Add($"hearing-{item.Id}", item.CaseId, item.Title, "hearing", item.HearingDate, "hearings");

        return rows.OrderBy(item => item.IsOverdue ? 0 : item.DueDate is null ? 5 : item.Urgency == "Due Today" ? 1 : item.Urgency == "Next 7 Days" ? 2 : item.Urgency == "Next 14 Days" ? 3 : 4)
            .ThenBy(item => item.DueDate ?? "9999-12-31")
            .ThenBy(item => item.CaseName)
            .Take(Math.Clamp(limit, 1, 10))
            .ToList();
    }

    private static bool IsOpenCase(CaseRecord record) => record.CaseStatus is "Pipeline" or "Filed / Service Pending" or "Active Litigation" or "Settlement Pending" or "Trial Preparation" && record.Status is not ("Closed" or "Complete" or "Triage");

    public async Task<long?> GetChildCaseIdAsync(string childKind,long id)
    {
        var table=childKind switch
        {
            "activity"=>"activity_log","case-note"=>"case_notes","hearing"=>"hearings","deadline"=>"deadlines",
            "checklist"=>"checklist_items","comparable-sale"=>"comparable_sales","witness"=>"witnesses",
            "exhibit"=>"exhibits","trial-motion"=>"trial_motions","case-issue-tag"=>"case_issue_tags",
            "document-export"=>"document_exports","risk-offer"=>"risk_analysis_offer_log",
            "service-log"=>"service_log_entries","publication-entry"=>"publication_dates","discovery"=>"discovery_tracking",
            "opposing-attorney"=>"case_opposing_attorneys",
            _=>throw new ArgumentException("Unknown child record kind.",nameof(childKind))
        };
        await using var connection=new SqliteConnection(ConnectionString);await connection.OpenAsync();await using var command=connection.CreateCommand();command.CommandText=$"SELECT case_id FROM {table} WHERE id=@id";command.Parameters.AddWithValue("@id",id);var value=await command.ExecuteScalarAsync();return value is null?null:Convert.ToInt64(value);
    }

    public async Task<CaseRecord> SaveCaseAsync(CaseRecord model)
    {
        if (DateOnly.TryParse(model.DateOpened, out var opened) && DateOnly.TryParse(model.ClosedDate, out var closed) && closed < opened)
            throw new ArgumentException("Date Closed cannot be before Date Opened.");
        return await WithWriteAsync(async (connection, tx) =>
        {
            await SaveCaseInternalAsync(connection, tx, model);
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Saved case {model.CaseNumber} at {DateTime.Now:G}");
            return model;
        });
    }

    public async Task DeleteCaseAsync(long caseId)
    {
        await WithWriteAsync(async (connection, tx) =>
        {
            var exportPaths = new List<string>();
            var exportCmd = connection.CreateCommand();
            exportCmd.Transaction = tx;
            exportCmd.CommandText = "SELECT output_path FROM document_exports WHERE case_id=@caseId";
            exportCmd.Parameters.AddWithValue("@caseId", caseId);
            await using (var reader = await exportCmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    if (!reader.IsDBNull(0))
                    {
                        exportPaths.Add(reader.GetString(0));
                    }
                }
            }

            foreach (var sql in new[]
                     {
                         "DELETE FROM deadline_history WHERE deadline_id IN (SELECT id FROM deadlines WHERE case_id=@caseId)",
                         "DELETE FROM deadlines WHERE case_id=@caseId",
                         "DELETE FROM checklist_items WHERE case_id=@caseId",
                         "DELETE FROM discovery_tracking WHERE case_id=@caseId",
                         "DELETE FROM publication_dates WHERE case_id=@caseId",
                         "DELETE FROM case_issue_tags WHERE case_id=@caseId",
                         "DELETE FROM case_notes WHERE case_id=@caseId",
                         "DELETE FROM hearings WHERE case_id=@caseId",
                         "DELETE FROM document_exports WHERE case_id=@caseId",
                         "DELETE FROM valuation_positions WHERE case_id=@caseId",
                         "DELETE FROM comparable_sales WHERE case_id=@caseId",
                         "DELETE FROM witnesses WHERE case_id=@caseId",
                         "DELETE FROM exhibits WHERE case_id=@caseId",
                         "DELETE FROM trial_motions WHERE case_id=@caseId",
                         "DELETE FROM risk_analyses WHERE case_id=@caseId",
                         "DELETE FROM risk_analysis_offer_log WHERE case_id=@caseId",
                         "DELETE FROM cases WHERE id=@caseId"
                     })
            {
                var cmd = connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                cmd.Parameters.AddWithValue("@caseId", caseId);
                await cmd.ExecuteNonQueryAsync();
            }

            foreach (var path in exportPaths.Where(File.Exists))
            {
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // Export cleanup is best effort only.
                }
            }

            await SetAppSettingAsync(connection, tx, "last_save_result", $"Deleted case {caseId} at {DateTime.Now:G}");
            return 0;
        });
    }

    public async Task<List<DeadlineItem>> GetDeadlinesAsync(long? caseId)
    {
        var list = new List<DeadlineItem>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, case_id, title, due_date, status, notes, source_type, is_manual, severity, completed_at,
                   source_kind, source_template_id, source_template_version, source_stage, generated_at, generated_by
            FROM deadlines
            WHERE (@caseId IS NULL OR case_id = @caseId)
            ORDER BY COALESCE(due_date, '9999-12-31'), title
            """;
        cmd.Parameters.AddWithValue("@caseId", caseId is null ? DBNull.Value : caseId);
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                list.Add(new DeadlineItem
                {
                    Id = reader.GetInt64(0),
                    CaseId = reader.GetInt64(1),
                    Title = reader.GetString(2),
                    DueDate = NormalizeDate(reader.IsDBNull(3) ? null : reader.GetString(3)),
                    Status = reader.IsDBNull(4) ? "Open" : reader.GetString(4),
                    Notes = reader.IsDBNull(5) ? null : reader.GetString(5),
                    SourceType = reader.IsDBNull(6) ? "Manual" : reader.GetString(6),
                    IsManual = !reader.IsDBNull(7) && reader.GetInt64(7) == 1,
                    Severity = reader.IsDBNull(8) ? "normal" : reader.GetString(8),
                    CompletedAt = reader.IsDBNull(9) ? null : reader.GetString(9),
                    SourceKind = reader.IsDBNull(10) ? "Manual" : reader.GetString(10),
                    SourceTemplateId = reader.IsDBNull(11) ? null : reader.GetString(11),
                    SourceTemplateVersion = reader.IsDBNull(12) ? null : reader.GetInt32(12),
                    SourceStage = reader.IsDBNull(13) ? null : reader.GetString(13),
                    GeneratedAt = reader.IsDBNull(14) ? null : reader.GetString(14),
                    GeneratedBy = reader.IsDBNull(15) ? null : reader.GetString(15)
                });
            }
        }

        if (list.Count > 0)
        {
            var historyByDeadline = new Dictionary<long, List<DeadlineHistoryEntry>>();
            var historyCmd = connection.CreateCommand();
            historyCmd.CommandText = caseId is null
                ? "SELECT deadline_id, previous_due_date, new_due_date, reason, created_at FROM deadline_history ORDER BY created_at"
                : """
                    SELECT h.deadline_id, h.previous_due_date, h.new_due_date, h.reason, h.created_at
                    FROM deadline_history h
                    JOIN deadlines d ON d.id = h.deadline_id
                    WHERE d.case_id = @caseId
                    ORDER BY h.created_at
                    """;
            if (caseId is not null)
            {
                historyCmd.Parameters.AddWithValue("@caseId", caseId);
            }

            await using var historyReader = await historyCmd.ExecuteReaderAsync();
            while (await historyReader.ReadAsync())
            {
                var deadlineId = historyReader.GetInt64(0);
                if (!historyByDeadline.TryGetValue(deadlineId, out var entries))
                {
                    entries = [];
                    historyByDeadline[deadlineId] = entries;
                }

                entries.Add(new DeadlineHistoryEntry
                {
                    PreviousDueDate = historyReader.IsDBNull(1) ? null : historyReader.GetString(1),
                    NewDueDate = historyReader.IsDBNull(2) ? null : historyReader.GetString(2),
                    Reason = historyReader.IsDBNull(3) ? null : historyReader.GetString(3),
                    ChangedAt = historyReader.GetString(4)
                });
            }

            foreach (var item in list)
            {
                if (historyByDeadline.TryGetValue(item.Id, out var entries))
                {
                    item.History = entries;
                }
            }
        }

        return list;
    }

    public async Task<DeadlineItem> SaveDeadlineAsync(DeadlineItem model)
    {
        return await WithWriteAsync(async (connection, tx) =>
        {
            var now = DateTime.UtcNow.ToString("O");
            string? previousDueDate = null;
            string? previousStatus = null;
            string? previousCompletedAt = null;
            if (model.Id != 0)
            {
                var lookup = connection.CreateCommand();
                lookup.Transaction = tx;
                lookup.CommandText = "SELECT due_date, status, completed_at FROM deadlines WHERE id=@id";
                lookup.Parameters.AddWithValue("@id", model.Id);
                await using var lookupReader = await lookup.ExecuteReaderAsync();
                if (await lookupReader.ReadAsync())
                {
                    previousDueDate = lookupReader.IsDBNull(0) ? null : lookupReader.GetString(0);
                    previousStatus = lookupReader.IsDBNull(1) ? null : lookupReader.GetString(1);
                    previousCompletedAt = lookupReader.IsDBNull(2) ? null : lookupReader.GetString(2);
                }
            }

            var isNowDone = model.Status is "Done" or "Complete";
            var wasAlreadyDone = previousStatus is "Done" or "Complete";
            var completedAt = isNowDone ? (wasAlreadyDone ? previousCompletedAt : now) : null;

            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            if (model.Id == 0)
            {
                cmd.CommandText = """
                    INSERT INTO deadlines (case_id, title, due_date, status, notes, source_type, is_manual, severity, created_at, updated_at, completed_at,
                        source_kind, source_template_id, source_template_version, source_stage, generated_at, generated_by)
                    VALUES (@case_id, @title, @due_date, @status, @notes, @source_type, @is_manual, @severity, @created_at, @updated_at, @completed_at,
                        @source_kind,@source_template_id,@source_template_version,@source_stage,@generated_at,@generated_by);
                    SELECT last_insert_rowid();
                    """;
            }
            else
            {
                cmd.CommandText = """
                    UPDATE deadlines
                    SET title=@title, due_date=@due_date, status=@status, notes=@notes, severity=@severity, updated_at=@updated_at, completed_at=@completed_at
                    WHERE id=@id;
                    SELECT @id;
                    """;
                cmd.Parameters.AddWithValue("@id", model.Id);
            }

            cmd.Parameters.AddWithValue("@case_id", model.CaseId);
            cmd.Parameters.AddWithValue("@title", model.Title);
            cmd.Parameters.AddWithValue("@due_date", DbValue(model.DueDate));
            cmd.Parameters.AddWithValue("@status", model.Status);
            cmd.Parameters.AddWithValue("@notes", DbValue(model.Notes));
            cmd.Parameters.AddWithValue("@source_type", model.SourceType);
            cmd.Parameters.AddWithValue("@is_manual", model.IsManual ? 1 : 0);
            cmd.Parameters.AddWithValue("@severity", string.IsNullOrWhiteSpace(model.Severity) ? "normal" : model.Severity.Trim());
            cmd.Parameters.AddWithValue("@created_at", now);
            cmd.Parameters.AddWithValue("@updated_at", now);
            cmd.Parameters.AddWithValue("@completed_at", DbValue(completedAt));
            cmd.Parameters.AddWithValue("@source_kind", model.SourceKind);
            cmd.Parameters.AddWithValue("@source_template_id", DbValue(model.SourceTemplateId));
            cmd.Parameters.AddWithValue("@source_template_version", model.SourceTemplateVersion.HasValue ? model.SourceTemplateVersion.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@source_stage", DbValue(model.SourceStage));
            cmd.Parameters.AddWithValue("@generated_at", DbValue(model.GeneratedAt));
            cmd.Parameters.AddWithValue("@generated_by", DbValue(model.GeneratedBy));
            model.Id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            model.CompletedAt = completedAt;

            var normalizedNewDue = NormalizeDate(model.DueDate);
            var normalizedPreviousDue = NormalizeDate(previousDueDate);
            if (previousDueDate is not null && normalizedNewDue != normalizedPreviousDue)
            {
                await LogDeadlineHistoryAsync(connection, tx, model.Id, normalizedPreviousDue, normalizedNewDue, model.ReasonForChange);
            }

            await SetAppSettingAsync(connection, tx, "last_save_result", $"Saved deadline {model.Title} at {DateTime.Now:G}");
            return model;
        });
    }

    private static async Task LogDeadlineHistoryAsync(SqliteConnection connection, SqliteTransaction tx, long deadlineId, string? previousDueDate, string? newDueDate, string? reason)
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO deadline_history (deadline_id, previous_due_date, new_due_date, reason, created_at)
            VALUES (@deadline_id, @previous_due_date, @new_due_date, @reason, @created_at)
            """;
        cmd.Parameters.AddWithValue("@deadline_id", deadlineId);
        cmd.Parameters.AddWithValue("@previous_due_date", DbValue(previousDueDate));
        cmd.Parameters.AddWithValue("@new_due_date", DbValue(newDueDate));
        cmd.Parameters.AddWithValue("@reason", DbValue(reason));
        cmd.Parameters.AddWithValue("@created_at", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<ChecklistItemRecord>> GetChecklistItemsAsync(long? caseId)
    {
        var list = new List<ChecklistItemRecord>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, case_id, phase, task, due_date, status, notes, source_type, is_manual, completed_at,
                   source_kind, source_template_id, source_template_version, source_stage, generated_at, generated_by, assigned_user_id
            FROM checklist_items
            WHERE (@caseId IS NULL OR case_id = @caseId)
            ORDER BY phase, task
            """;
        cmd.Parameters.AddWithValue("@caseId", caseId is null ? DBNull.Value : caseId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new ChecklistItemRecord
            {
                Id = reader.GetInt64(0),
                CaseId = reader.GetInt64(1),
                Phase = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Task = reader.GetString(3),
                DueDate = NormalizeDate(reader.IsDBNull(4) ? null : reader.GetString(4)),
                Status = reader.IsDBNull(5) ? "Not Started" : reader.GetString(5),
                Notes = reader.IsDBNull(6) ? null : reader.GetString(6),
                SourceType = reader.IsDBNull(7) ? "Manual" : reader.GetString(7),
                IsManual = !reader.IsDBNull(8) && reader.GetInt64(8) == 1,
                CompletedAt = reader.IsDBNull(9) ? null : reader.GetString(9),
                SourceKind = reader.IsDBNull(10) ? "Manual" : reader.GetString(10),
                SourceTemplateId = reader.IsDBNull(11) ? null : reader.GetString(11),
                SourceTemplateVersion = reader.IsDBNull(12) ? null : reader.GetInt32(12),
                SourceStage = reader.IsDBNull(13) ? null : reader.GetString(13),
                GeneratedAt = reader.IsDBNull(14) ? null : reader.GetString(14),
                GeneratedBy = reader.IsDBNull(15) ? null : reader.GetString(15),
                AssignedUserId = reader.IsDBNull(16) ? null : reader.GetString(16)
            });
        }

        return list;
    }

    public async Task<ChecklistItemRecord> SaveChecklistItemAsync(ChecklistItemRecord model)
    {
        return await WithWriteAsync(async (connection, tx) =>
        {
            var now = DateTime.UtcNow.ToString("O");
            string? previousStatus = null;
            string? previousCompletedAt = null;
            if (model.Id != 0)
            {
                var lookup = connection.CreateCommand();
                lookup.Transaction = tx;
                lookup.CommandText = "SELECT status, completed_at FROM checklist_items WHERE id=@id";
                lookup.Parameters.AddWithValue("@id", model.Id);
                await using var lookupReader = await lookup.ExecuteReaderAsync();
                if (await lookupReader.ReadAsync())
                {
                    previousStatus = lookupReader.IsDBNull(0) ? null : lookupReader.GetString(0);
                    previousCompletedAt = lookupReader.IsDBNull(1) ? null : lookupReader.GetString(1);
                }
            }

            var isNowDone = model.Status is "Done" or "Complete";
            var wasAlreadyDone = previousStatus is "Done" or "Complete";
            var completedAt = isNowDone ? (wasAlreadyDone ? previousCompletedAt : now) : null;

            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            if (model.Id == 0)
            {
                cmd.CommandText = """
                    INSERT INTO checklist_items (case_id, phase, task, due_date, status, notes, source_type, is_manual, created_at, updated_at, completed_at,
                        source_kind, source_template_id, source_template_version, source_stage, generated_at, generated_by, assigned_user_id)
                    VALUES (@case_id, @phase, @task, @due_date, @status, @notes, @source_type, @is_manual, @created_at, @updated_at, @completed_at,
                        @source_kind,@source_template_id,@source_template_version,@source_stage,@generated_at,@generated_by,@assigned_user_id);
                    SELECT last_insert_rowid();
                    """;
            }
            else
            {
                cmd.CommandText = """
                    UPDATE checklist_items
                    SET phase=@phase, task=@task, due_date=@due_date, status=@status, notes=@notes, updated_at=@updated_at, completed_at=@completed_at, assigned_user_id=@assigned_user_id
                    WHERE id=@id;
                    SELECT @id;
                    """;
                cmd.Parameters.AddWithValue("@id", model.Id);
            }

            cmd.Parameters.AddWithValue("@case_id", model.CaseId);
            cmd.Parameters.AddWithValue("@phase", model.Phase);
            cmd.Parameters.AddWithValue("@task", model.Task);
            cmd.Parameters.AddWithValue("@due_date", DbValue(model.DueDate));
            cmd.Parameters.AddWithValue("@status", model.Status);
            cmd.Parameters.AddWithValue("@notes", DbValue(model.Notes));
            cmd.Parameters.AddWithValue("@source_type", model.SourceType);
            cmd.Parameters.AddWithValue("@is_manual", model.IsManual ? 1 : 0);
            cmd.Parameters.AddWithValue("@created_at", now);
            cmd.Parameters.AddWithValue("@updated_at", now);
            cmd.Parameters.AddWithValue("@completed_at", DbValue(completedAt));
            cmd.Parameters.AddWithValue("@source_kind", model.SourceKind);
            cmd.Parameters.AddWithValue("@source_template_id", DbValue(model.SourceTemplateId));
            cmd.Parameters.AddWithValue("@source_template_version", model.SourceTemplateVersion.HasValue ? model.SourceTemplateVersion.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@source_stage", DbValue(model.SourceStage));
            cmd.Parameters.AddWithValue("@generated_at", DbValue(model.GeneratedAt));
            cmd.Parameters.AddWithValue("@generated_by", DbValue(model.GeneratedBy));
            cmd.Parameters.AddWithValue("@assigned_user_id", DbValue(model.AssignedUserId));
            model.Id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            model.CompletedAt = completedAt;
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Saved checklist task {model.Task} at {DateTime.Now:G}");
            return model;
        });
    }

    public async Task DeleteChecklistItemAsync(long id)
    {
        await WithWriteAsync(async (connection, tx) =>
        {
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM checklist_items WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Deleted checklist item {id} at {DateTime.Now:G}");
            return 0;
        });
    }

    public async Task DeleteDeadlineAsync(long id)
    {
        await WithWriteAsync(async (connection, tx) =>
        {
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM deadlines WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Deleted deadline {id} at {DateTime.Now:G}");
            return 0;
        });
    }

    public async Task<List<DiscoveryItemRecord>> GetDiscoveryItemsAsync(long? caseId)
    {
        var list = new List<DiscoveryItemRecord>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, case_id, request_title, direction, discovery_type, served_date, due_date, response_date, follow_up_date, status, assigned_to, notes, escalation_note, good_faith_sent_date, motion_to_compel_date
            FROM discovery_tracking
            WHERE (@caseId IS NULL OR case_id = @caseId)
            ORDER BY COALESCE(due_date, '9999-12-31')
            """;
        cmd.Parameters.AddWithValue("@caseId", caseId is null ? DBNull.Value : caseId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new DiscoveryItemRecord
            {
                Id = reader.GetInt64(0),
                CaseId = reader.GetInt64(1),
                RequestTitle = reader.IsDBNull(2) ? null : reader.GetString(2),
                Direction = reader.IsDBNull(3) ? "Served by Us" : reader.GetString(3),
                DiscoveryType = reader.IsDBNull(4) ? "Interrogatories" : reader.GetString(4),
                ServedDate = NormalizeDate(reader.IsDBNull(5) ? null : reader.GetString(5)),
                DueDate = NormalizeDate(reader.IsDBNull(6) ? null : reader.GetString(6)),
                ResponseDate = NormalizeDate(reader.IsDBNull(7) ? null : reader.GetString(7)),
                FollowUpDate = NormalizeDate(reader.IsDBNull(8) ? null : reader.GetString(8)),
                Status = reader.IsDBNull(9) ? "Waiting for Responses" : reader.GetString(9),
                AssignedTo = reader.IsDBNull(10) ? null : reader.GetString(10),
                Notes = reader.IsDBNull(11) ? null : reader.GetString(11),
                EscalationNote = reader.IsDBNull(12) ? null : reader.GetString(12),
                GoodFaithSentDate = NormalizeDate(reader.IsDBNull(13) ? null : reader.GetString(13)),
                MotionToCompelDate = NormalizeDate(reader.IsDBNull(14) ? null : reader.GetString(14))
            });
        }

        return list;
    }

    public async Task<DiscoveryItemRecord> SaveDiscoveryItemAsync(DiscoveryItemRecord model)
    {
        return await WithWriteAsync(async (connection, tx) =>
        {
            var now = DateTime.UtcNow.ToString("O");
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            if (model.Id == 0)
            {
                cmd.CommandText = """
                    INSERT INTO discovery_tracking (
                        case_id, request_title, direction, discovery_type, served_date, due_date, response_date,
                        follow_up_date, status, assigned_to, notes, escalation_note, good_faith_sent_date, motion_to_compel_date, created_at, updated_at
                    )
                    VALUES (
                        @case_id, @request_title, @direction, @discovery_type, @served_date, @due_date, @response_date,
                        @follow_up_date, @status, @assigned_to, @notes, @escalation_note, @good_faith_sent_date, @motion_to_compel_date, @created_at, @updated_at
                    );
                    SELECT last_insert_rowid();
                    """;
            }
            else
            {
                cmd.CommandText = """
                    UPDATE discovery_tracking
                    SET request_title=@request_title, direction=@direction, discovery_type=@discovery_type, served_date=@served_date, due_date=@due_date,
                        response_date=@response_date, follow_up_date=@follow_up_date, status=@status, assigned_to=@assigned_to,
                        notes=@notes, escalation_note=@escalation_note, good_faith_sent_date=@good_faith_sent_date, motion_to_compel_date=@motion_to_compel_date, updated_at=@updated_at
                    WHERE id=@id;
                    SELECT @id;
                    """;
                cmd.Parameters.AddWithValue("@id", model.Id);
            }

            cmd.Parameters.AddWithValue("@case_id", model.CaseId);
            cmd.Parameters.AddWithValue("@request_title", DbValue(model.RequestTitle));
            cmd.Parameters.AddWithValue("@direction", model.Direction);
            cmd.Parameters.AddWithValue("@discovery_type", model.DiscoveryType);
            cmd.Parameters.AddWithValue("@served_date", DbValue(model.ServedDate));
            cmd.Parameters.AddWithValue("@due_date", DbValue(model.DueDate));
            cmd.Parameters.AddWithValue("@response_date", DbValue(model.ResponseDate));
            cmd.Parameters.AddWithValue("@follow_up_date", DbValue(model.FollowUpDate));
            cmd.Parameters.AddWithValue("@status", model.Status);
            cmd.Parameters.AddWithValue("@assigned_to", DbValue(model.AssignedTo));
            cmd.Parameters.AddWithValue("@notes", DbValue(model.Notes));
            cmd.Parameters.AddWithValue("@escalation_note", DbValue(model.EscalationNote));
            cmd.Parameters.AddWithValue("@good_faith_sent_date", DbValue(model.GoodFaithSentDate));
            cmd.Parameters.AddWithValue("@motion_to_compel_date", DbValue(model.MotionToCompelDate));
            cmd.Parameters.AddWithValue("@created_at", now);
            cmd.Parameters.AddWithValue("@updated_at", now);
            model.Id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Saved discovery item {model.DiscoveryType} at {DateTime.Now:G}");
            return model;
        });
    }

    public async Task DeleteDiscoveryItemAsync(long id)
    {
        await WithWriteAsync(async (connection, tx) =>
        {
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM discovery_tracking WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Deleted discovery item {id} at {DateTime.Now:G}");
            return 0;
        });
    }

    public async Task<List<ValuationPositionRecord>> GetValuationPositionsAsync(long? caseId)
    {
        var list = new List<ValuationPositionRecord>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, case_id, side, appraiser_name, appraised_value, value_date, methodology, notes, updated_at
            FROM valuation_positions
            WHERE (@caseId IS NULL OR case_id = @caseId)
            ORDER BY side
            """;
        cmd.Parameters.AddWithValue("@caseId", caseId is null ? DBNull.Value : caseId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new ValuationPositionRecord
            {
                Id = reader.GetInt64(0),
                CaseId = reader.GetInt64(1),
                Side = reader.GetString(2),
                AppraiserName = reader.IsDBNull(3) ? null : reader.GetString(3),
                AppraisedValue = reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                ValueDate = NormalizeDate(reader.IsDBNull(5) ? null : reader.GetString(5)),
                Methodology = reader.IsDBNull(6) ? null : reader.GetString(6),
                Notes = reader.IsDBNull(7) ? null : reader.GetString(7),
                UpdatedAt = reader.IsDBNull(8) ? null : reader.GetString(8)
            });
        }

        return list;
    }

    // At most one row per (case, side): editing a side always overwrites that side's row.
    public async Task<ValuationPositionRecord> SaveValuationPositionAsync(ValuationPositionRecord model)
    {
        return await WithWriteAsync(async (connection, tx) =>
        {
            var now = DateTime.UtcNow.ToString("O");
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO valuation_positions (case_id, side, appraiser_name, appraised_value, value_date, methodology, notes, created_at, updated_at)
                VALUES (@case_id, @side, @appraiser_name, @appraised_value, @value_date, @methodology, @notes, @now, @now)
                ON CONFLICT(case_id, side) DO UPDATE SET
                    appraiser_name = excluded.appraiser_name,
                    appraised_value = excluded.appraised_value,
                    value_date = excluded.value_date,
                    methodology = excluded.methodology,
                    notes = excluded.notes,
                    updated_at = excluded.updated_at;
                SELECT id FROM valuation_positions WHERE case_id = @case_id AND side = @side;
                """;
            cmd.Parameters.AddWithValue("@case_id", model.CaseId);
            cmd.Parameters.AddWithValue("@side", model.Side);
            cmd.Parameters.AddWithValue("@appraiser_name", DbValue(model.AppraiserName));
            cmd.Parameters.AddWithValue("@appraised_value", model.AppraisedValue.HasValue ? model.AppraisedValue.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@value_date", DbValue(model.ValueDate));
            cmd.Parameters.AddWithValue("@methodology", DbValue(model.Methodology));
            cmd.Parameters.AddWithValue("@notes", DbValue(model.Notes));
            cmd.Parameters.AddWithValue("@now", now);
            model.Id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            model.UpdatedAt = now;
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Saved {model.Side} valuation position at {DateTime.Now:G}");
            return model;
        });
    }

    // --- Attorney Dashboard: discovery posture, pipeline handoffs, activity log ---

    public async Task<List<DiscoveryPosture>> GetDiscoveryPosturesAsync(long? caseId)
    {
        var list = new List<DiscoveryPosture>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, case_id, strategy, strategy_reason, strategy_selected_date, discovery_served_date,
                   responses_due_date, responses_received_date, responses_reviewed_date, discovery_cutoff_date,
                   planned_depositions, deficiency_status, next_decision, next_review_date, is_complete,
                   created_at, updated_at, completion_changed_at, completion_changed_by
            FROM discovery_postures
            WHERE (@caseId IS NULL OR case_id = @caseId)
            """;
        cmd.Parameters.AddWithValue("@caseId", caseId is null ? DBNull.Value : caseId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new DiscoveryPosture
            {
                Id = reader.GetInt64(0),
                CaseId = reader.GetInt64(1),
                Strategy = reader.IsDBNull(2) ? "Strategy not selected" : reader.GetString(2),
                StrategyReason = reader.IsDBNull(3) ? null : reader.GetString(3),
                StrategySelectedDate = NormalizeDate(reader.IsDBNull(4) ? null : reader.GetString(4)),
                DiscoveryServedDate = NormalizeDate(reader.IsDBNull(5) ? null : reader.GetString(5)),
                ResponsesDueDate = NormalizeDate(reader.IsDBNull(6) ? null : reader.GetString(6)),
                ResponsesReceivedDate = NormalizeDate(reader.IsDBNull(7) ? null : reader.GetString(7)),
                ResponsesReviewedDate = NormalizeDate(reader.IsDBNull(8) ? null : reader.GetString(8)),
                DiscoveryCutoffDate = NormalizeDate(reader.IsDBNull(9) ? null : reader.GetString(9)),
                PlannedDepositions = reader.IsDBNull(10) ? null : reader.GetString(10),
                DeficiencyStatus = reader.IsDBNull(11) ? null : reader.GetString(11),
                NextDecision = reader.IsDBNull(12) ? null : reader.GetString(12),
                NextReviewDate = NormalizeDate(reader.IsDBNull(13) ? null : reader.GetString(13)),
                IsComplete = !reader.IsDBNull(14) && reader.GetInt64(14) == 1,
                CreatedAt = reader.IsDBNull(15) ? null : reader.GetString(15),
                UpdatedAt = reader.IsDBNull(16) ? null : reader.GetString(16),
                CompletionChangedAt = reader.IsDBNull(17) ? null : reader.GetString(17),
                CompletionChangedBy = reader.IsDBNull(18) ? null : reader.GetString(18)
            });
        }

        return list;
    }

    public async Task<DiscoveryPosture?> GetDiscoveryPostureAsync(long caseId) =>
        (await GetDiscoveryPosturesAsync(caseId)).FirstOrDefault();

    // At most one posture row per case - editing always overwrites, same ON CONFLICT pattern as
    // SaveValuationPositionAsync above.
    public async Task<DiscoveryPosture> SaveDiscoveryPostureAsync(DiscoveryPosture model)
    {
        var previous = await GetDiscoveryPostureAsync(model.CaseId);
        var completionChanged = previous is null || previous.IsComplete != model.IsComplete;
        var result = await WithWriteAsync(async (connection, tx) =>
        {
            var now = DateTime.UtcNow.ToString("O");
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO discovery_postures (
                    case_id, strategy, strategy_reason, strategy_selected_date, discovery_served_date,
                    responses_due_date, responses_received_date, responses_reviewed_date, discovery_cutoff_date,
                    planned_depositions, deficiency_status, next_decision, next_review_date, is_complete,
                    created_at, updated_at, completion_changed_at, completion_changed_by
                ) VALUES (
                    @case_id, @strategy, @strategy_reason, @strategy_selected_date, @discovery_served_date,
                    @responses_due_date, @responses_received_date, @responses_reviewed_date, @discovery_cutoff_date,
                    @planned_depositions, @deficiency_status, @next_decision, @next_review_date, @is_complete,
                    @now, @now, @completion_changed_at, @completion_changed_by
                )
                ON CONFLICT(case_id) DO UPDATE SET
                    strategy = excluded.strategy,
                    strategy_reason = excluded.strategy_reason,
                    strategy_selected_date = excluded.strategy_selected_date,
                    discovery_served_date = excluded.discovery_served_date,
                    responses_due_date = excluded.responses_due_date,
                    responses_received_date = excluded.responses_received_date,
                    responses_reviewed_date = excluded.responses_reviewed_date,
                    discovery_cutoff_date = excluded.discovery_cutoff_date,
                    planned_depositions = excluded.planned_depositions,
                    deficiency_status = excluded.deficiency_status,
                    next_decision = excluded.next_decision,
                    next_review_date = excluded.next_review_date,
                    is_complete = excluded.is_complete,
                    updated_at = excluded.updated_at,
                    completion_changed_at = CASE WHEN discovery_postures.is_complete <> excluded.is_complete THEN excluded.completion_changed_at ELSE discovery_postures.completion_changed_at END,
                    completion_changed_by = CASE WHEN discovery_postures.is_complete <> excluded.is_complete THEN excluded.completion_changed_by ELSE discovery_postures.completion_changed_by END;
                SELECT id FROM discovery_postures WHERE case_id = @case_id;
                """;
            cmd.Parameters.AddWithValue("@case_id", model.CaseId);
            cmd.Parameters.AddWithValue("@strategy", string.IsNullOrWhiteSpace(model.Strategy) ? "Strategy not selected" : model.Strategy);
            cmd.Parameters.AddWithValue("@strategy_reason", DbValue(model.StrategyReason));
            cmd.Parameters.AddWithValue("@strategy_selected_date", DbValue(model.StrategySelectedDate));
            cmd.Parameters.AddWithValue("@discovery_served_date", DbValue(model.DiscoveryServedDate));
            cmd.Parameters.AddWithValue("@responses_due_date", DbValue(model.ResponsesDueDate));
            cmd.Parameters.AddWithValue("@responses_received_date", DbValue(model.ResponsesReceivedDate));
            cmd.Parameters.AddWithValue("@responses_reviewed_date", DbValue(model.ResponsesReviewedDate));
            cmd.Parameters.AddWithValue("@discovery_cutoff_date", DbValue(model.DiscoveryCutoffDate));
            cmd.Parameters.AddWithValue("@planned_depositions", DbValue(model.PlannedDepositions));
            cmd.Parameters.AddWithValue("@deficiency_status", DbValue(model.DeficiencyStatus));
            cmd.Parameters.AddWithValue("@next_decision", DbValue(model.NextDecision));
            cmd.Parameters.AddWithValue("@next_review_date", DbValue(model.NextReviewDate));
            cmd.Parameters.AddWithValue("@is_complete", model.IsComplete ? 1 : 0);
            cmd.Parameters.AddWithValue("@now", now);
            cmd.Parameters.AddWithValue("@completion_changed_at", completionChanged ? now : DbValue(model.CompletionChangedAt));
            cmd.Parameters.AddWithValue("@completion_changed_by", completionChanged ? _actor.AuditLabel : DbValue(model.CompletionChangedBy));
            model.Id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            model.UpdatedAt = now;
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Saved discovery posture for case {model.CaseId} at {DateTime.Now:G}");
            return model;
        });
        if (completionChanged)
        {
            await RecordActivityAsync(model.CaseId, model.IsComplete ? "DiscoveryCompleted" : "DiscoveryReopened",
                model.IsComplete ? "Discovery marked complete." : "Discovery marked incomplete.", null);
        }
        return result;
    }

    public async Task<List<PipelineHandoffRecord>> GetPipelineHandoffsAsync(long caseId)
    {
        var list = new List<PipelineHandoffRecord>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, case_id, previous_holder, new_holder, previous_stage, new_stage, handoff_date, next_review_date, note, created_at
            FROM pipeline_handoffs
            WHERE case_id = @caseId
            ORDER BY created_at DESC
            """;
        cmd.Parameters.AddWithValue("@caseId", caseId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new PipelineHandoffRecord
            {
                Id = reader.GetInt64(0),
                CaseId = reader.GetInt64(1),
                PreviousHolder = reader.IsDBNull(2) ? null : reader.GetString(2),
                NewHolder = reader.IsDBNull(3) ? "" : reader.GetString(3),
                PreviousStage = reader.IsDBNull(4) ? null : reader.GetString(4),
                NewStage = reader.IsDBNull(5) ? "" : reader.GetString(5),
                HandoffDate = NormalizeDate(reader.IsDBNull(6) ? null : reader.GetString(6)),
                NextReviewDate = NormalizeDate(reader.IsDBNull(7) ? null : reader.GetString(7)),
                Note = reader.IsDBNull(8) ? null : reader.GetString(8),
                CreatedAt = reader.IsDBNull(9) ? null : reader.GetString(9)
            });
        }

        return list;
    }

    // Records the handoff history row AND updates the case's current holder/stage/next-review in
    // one transaction, per the dashboard brief's "each handoff should automatically create a
    // history entry" requirement.
    public async Task<PipelineHandoffRecord> SavePipelineHandoffAsync(long caseId, PipelineHandoffRequest request)
    {
        return await WithWriteAsync(async (connection, tx) =>
        {
            var now = DateTime.UtcNow.ToString("O");
            var caseCmd = connection.CreateCommand();
            caseCmd.Transaction = tx;
            caseCmd.CommandText = "SELECT current_holder, pipeline_stage FROM cases WHERE id=@id";
            caseCmd.Parameters.AddWithValue("@id", caseId);
            string? previousHolder = null;
            string? previousStage = null;
            await using (var reader = await caseCmd.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    previousHolder = reader.IsDBNull(0) ? null : reader.GetString(0);
                    previousStage = reader.IsDBNull(1) ? null : reader.GetString(1);
                }
            }

            var handoffDate = string.IsNullOrWhiteSpace(request.HandoffDate) ? now[..10] : request.HandoffDate;
            var insertCmd = connection.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText = """
                INSERT INTO pipeline_handoffs (case_id, previous_holder, new_holder, previous_stage, new_stage, handoff_date, next_review_date, note, created_at)
                VALUES (@case_id, @previous_holder, @new_holder, @previous_stage, @new_stage, @handoff_date, @next_review_date, @note, @now);
                SELECT last_insert_rowid();
                """;
            insertCmd.Parameters.AddWithValue("@case_id", caseId);
            insertCmd.Parameters.AddWithValue("@previous_holder", DbValue(previousHolder));
            insertCmd.Parameters.AddWithValue("@new_holder", request.NewHolder ?? "");
            insertCmd.Parameters.AddWithValue("@previous_stage", DbValue(previousStage));
            insertCmd.Parameters.AddWithValue("@new_stage", request.NewStage ?? "");
            insertCmd.Parameters.AddWithValue("@handoff_date", handoffDate);
            insertCmd.Parameters.AddWithValue("@next_review_date", DbValue(request.NextReviewDate));
            insertCmd.Parameters.AddWithValue("@note", DbValue(request.Note));
            insertCmd.Parameters.AddWithValue("@now", now);
            var handoffId = Convert.ToInt64(await insertCmd.ExecuteScalarAsync());

            var updateCmd = connection.CreateCommand();
            updateCmd.Transaction = tx;
            updateCmd.CommandText = """
                UPDATE cases SET current_holder=@new_holder, pipeline_stage=@new_stage,
                                  date_sent_to_current_holder=@handoff_date, next_review_date=@next_review_date,
                                  updated_at=@now
                WHERE id=@id
                """;
            updateCmd.Parameters.AddWithValue("@new_holder", request.NewHolder ?? "");
            updateCmd.Parameters.AddWithValue("@new_stage", request.NewStage ?? "");
            updateCmd.Parameters.AddWithValue("@handoff_date", handoffDate);
            updateCmd.Parameters.AddWithValue("@next_review_date", DbValue(request.NextReviewDate));
            updateCmd.Parameters.AddWithValue("@now", now);
            updateCmd.Parameters.AddWithValue("@id", caseId);
            await updateCmd.ExecuteNonQueryAsync();

            await SetAppSettingAsync(connection, tx, "last_save_result", $"Pipeline handoff for case {caseId} to {request.NewHolder} at {DateTime.Now:G}");
            return new PipelineHandoffRecord
            {
                Id = handoffId,
                CaseId = caseId,
                PreviousHolder = previousHolder,
                NewHolder = request.NewHolder ?? "",
                PreviousStage = previousStage,
                NewStage = request.NewStage ?? "",
                HandoffDate = handoffDate,
                NextReviewDate = request.NextReviewDate,
                Note = request.Note,
                CreatedAt = now
            };
        });
    }

    private static readonly HashSet<string> MeaningfulActivityTypes = new(StringComparer.Ordinal)
    {
        "ComplaintFiled", "AnswerFiled", "ServiceCompleted", "PublicationCompleted", "DiscoveryServed",
        "DiscoveryResponsesReceived", "DiscoveryResponsesReviewed", "DepositionHeld", "AppraisalReceived",
        "AppraisalReviewed", "NegotiationPositionChanged", "SettlementAuthorityRequested",
        "SettlementAuthorityReceived", "MotionFiled", "MotionDecided", "MediationScheduled", "MediationHeld",
        "TrialPrepMilestoneCompleted", "AttorneyStrategyDecisionRecorded",
    };

    // The one place last_meaningful_activity_date gets written - everything else that touches a
    // case (routine field edits, tag changes, renames) does NOT call this, so the momentum clock
    // only resets on a real qualifying event. "Other" activity types are logged (visible in the
    // case's activity history) but never treated as meaningful.
    public async Task<ActivityLogEntry> RecordActivityAsync(long caseId, string activityType, string? notes, string? occurredAt)
    {
        return await WithWriteAsync(async (connection, tx) =>
        {
            var now = DateTime.UtcNow.ToString("O");
            activityType = string.IsNullOrWhiteSpace(activityType) ? "Other" : activityType;
            var isMeaningful = MeaningfulActivityTypes.Contains(activityType);
            var occurred = string.IsNullOrWhiteSpace(occurredAt) ? now : occurredAt;

            var insertCmd = connection.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText = """
                INSERT INTO activity_log (case_id, activity_type, is_meaningful, occurred_at, notes, created_at, actor_user_id, actor_display)
                VALUES (@case_id, @activity_type, @is_meaningful, @occurred_at, @notes, @now, @actor_user_id, @actor_display);
                SELECT last_insert_rowid();
                """;
            insertCmd.Parameters.AddWithValue("@case_id", caseId);
            insertCmd.Parameters.AddWithValue("@activity_type", activityType);
            insertCmd.Parameters.AddWithValue("@is_meaningful", isMeaningful ? 1 : 0);
            insertCmd.Parameters.AddWithValue("@occurred_at", occurred);
            insertCmd.Parameters.AddWithValue("@notes", DbValue(notes));
            insertCmd.Parameters.AddWithValue("@now", now);
            insertCmd.Parameters.AddWithValue("@actor_user_id",(object?)_actor.UserId?.ToString("D")??DBNull.Value);
            insertCmd.Parameters.AddWithValue("@actor_display",_actor.AuditLabel);
            var id = Convert.ToInt64(await insertCmd.ExecuteScalarAsync());

            if (isMeaningful)
            {
                var updateCmd = connection.CreateCommand();
                updateCmd.Transaction = tx;
                updateCmd.CommandText = """
                    UPDATE cases SET last_meaningful_activity_date=@occurred_at
                    WHERE id=@id AND (last_meaningful_activity_date IS NULL OR last_meaningful_activity_date < @occurred_at)
                    """;
                updateCmd.Parameters.AddWithValue("@occurred_at", occurred);
                updateCmd.Parameters.AddWithValue("@id", caseId);
                await updateCmd.ExecuteNonQueryAsync();
            }

            return new ActivityLogEntry
            {
                Id = id,
                CaseId = caseId,
                ActivityType = activityType,
                IsMeaningful = isMeaningful,
                OccurredAt = occurred,
                Notes = notes,
                CreatedAt = now,ActorUserId=_actor.UserId?.ToString("D"),ActorDisplay=_actor.AuditLabel
            };
        });
    }

    public async Task<List<ActivityLogEntry>> GetActivityLogAsync(long? caseId)
    {
        var list = new List<ActivityLogEntry>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, case_id, activity_type, is_meaningful, occurred_at, notes, created_at, actor_user_id, actor_display
            FROM activity_log
            WHERE (@caseId IS NULL OR case_id = @caseId)
            ORDER BY occurred_at DESC
            """;
        cmd.Parameters.Add("@caseId", SqliteType.Integer).Value = (object?)caseId ?? DBNull.Value;
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                list.Add(new ActivityLogEntry
                {
                    Id = reader.GetInt64(0),
                    CaseId = reader.GetInt64(1),
                    ActivityType = reader.IsDBNull(2) ? "Other" : reader.GetString(2),
                    IsMeaningful = !reader.IsDBNull(3) && reader.GetInt64(3) == 1,
                    OccurredAt = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    Notes = reader.IsDBNull(5) ? null : reader.GetString(5),
                    CreatedAt = reader.IsDBNull(6) ? null : reader.GetString(6),
                    ActorUserId=reader.IsDBNull(7)?null:reader.GetString(7),ActorDisplay=reader.IsDBNull(8)?null:reader.GetString(8)
                });
            }
        }

        // Attach edit history (same ride-along pattern as GetDeadlinesAsync's deadline_history).
        if (list.Count > 0)
        {
            var byId = list.ToDictionary(e => e.Id);
            var historyCmd = connection.CreateCommand();
            historyCmd.CommandText = """
                SELECT h.id, h.activity_id, h.previous_type, h.new_type, h.previous_occurred_at,
                       h.new_occurred_at, h.previous_notes, h.new_notes, h.reason, h.created_at, h.edited_by_user_id, h.edited_by_display
                FROM activity_log_history h
                JOIN activity_log a ON a.id = h.activity_id
                WHERE (@caseId IS NULL OR a.case_id = @caseId)
                ORDER BY h.created_at
                """;
            historyCmd.Parameters.Add("@caseId", SqliteType.Integer).Value = (object?)caseId ?? DBNull.Value;
            await using var historyReader = await historyCmd.ExecuteReaderAsync();
            while (await historyReader.ReadAsync())
            {
                var activityId = historyReader.GetInt64(1);
                if (byId.TryGetValue(activityId, out var entry))
                {
                    entry.History.Add(new ActivityLogHistoryEntry
                    {
                        Id = historyReader.GetInt64(0),
                        ActivityId = activityId,
                        PreviousType = historyReader.IsDBNull(2) ? null : historyReader.GetString(2),
                        NewType = historyReader.IsDBNull(3) ? null : historyReader.GetString(3),
                        PreviousOccurredAt = historyReader.IsDBNull(4) ? null : historyReader.GetString(4),
                        NewOccurredAt = historyReader.IsDBNull(5) ? null : historyReader.GetString(5),
                        PreviousNotes = historyReader.IsDBNull(6) ? null : historyReader.GetString(6),
                        NewNotes = historyReader.IsDBNull(7) ? null : historyReader.GetString(7),
                        Reason = historyReader.IsDBNull(8) ? null : historyReader.GetString(8),
                        CreatedAt = historyReader.IsDBNull(9) ? null : historyReader.GetString(9),
                        EditedByUserId=historyReader.IsDBNull(10)?null:historyReader.GetString(10),EditedByDisplay=historyReader.IsDBNull(11)?null:historyReader.GetString(11),
                    });
                }
            }
        }

        return list;
    }

    // Edits an activity entry with the same no-silent-overwrite contract as deadline date
    // changes: the prior values, new values, reason, and timestamp are preserved in
    // activity_log_history before the entry is updated. Editing a meaningful entry's date (or
    // changing its type) honestly moves the momentum clock: last_meaningful_activity_date is
    // recomputed from the full log afterward rather than patched incrementally.
    public async Task<ActivityLogEntry> UpdateActivityEntryAsync(long activityId, UpdateActivityRequest request)
    {
        return await WithWriteAsync(async (connection, tx) =>
        {
            var readCmd = connection.CreateCommand();
            readCmd.Transaction = tx;
            readCmd.CommandText = "SELECT case_id, activity_type, occurred_at, notes FROM activity_log WHERE id=@id";
            readCmd.Parameters.AddWithValue("@id", activityId);
            long caseId;
            string previousType;
            string previousOccurredAt;
            string? previousNotes;
            await using (var reader = await readCmd.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync())
                {
                    throw new InvalidOperationException("Activity entry not found.");
                }

                caseId = reader.GetInt64(0);
                previousType = reader.IsDBNull(1) ? "Other" : reader.GetString(1);
                previousOccurredAt = reader.IsDBNull(2) ? "" : reader.GetString(2);
                previousNotes = reader.IsDBNull(3) ? null : reader.GetString(3);
            }

            var newType = string.IsNullOrWhiteSpace(request.ActivityType) ? "Other" : request.ActivityType;
            var newOccurredAt = string.IsNullOrWhiteSpace(request.OccurredAt) ? previousOccurredAt : request.OccurredAt;
            var newNotes = BlankToNull(request.Notes);
            var changed = newType != previousType || newOccurredAt != previousOccurredAt || newNotes != previousNotes;
            var now = DateTime.UtcNow.ToString("O");

            if (changed)
            {
                var historyCmd = connection.CreateCommand();
                historyCmd.Transaction = tx;
                historyCmd.CommandText = """
                    INSERT INTO activity_log_history (activity_id, previous_type, new_type, previous_occurred_at, new_occurred_at, previous_notes, new_notes, reason, created_at, edited_by_user_id, edited_by_display)
                    VALUES (@activity_id, @previous_type, @new_type, @previous_occurred_at, @new_occurred_at, @previous_notes, @new_notes, @reason, @now, @edited_by_user_id, @edited_by_display)
                    """;
                historyCmd.Parameters.AddWithValue("@activity_id", activityId);
                historyCmd.Parameters.AddWithValue("@previous_type", previousType);
                historyCmd.Parameters.AddWithValue("@new_type", newType);
                historyCmd.Parameters.AddWithValue("@previous_occurred_at", previousOccurredAt);
                historyCmd.Parameters.AddWithValue("@new_occurred_at", newOccurredAt);
                historyCmd.Parameters.AddWithValue("@previous_notes", DbValue(previousNotes));
                historyCmd.Parameters.AddWithValue("@new_notes", DbValue(newNotes));
                historyCmd.Parameters.AddWithValue("@reason", DbValue(request.Reason));
                historyCmd.Parameters.AddWithValue("@now",now);
                historyCmd.Parameters.AddWithValue("@edited_by_user_id",(object?)_actor.UserId?.ToString("D")??DBNull.Value);
                historyCmd.Parameters.AddWithValue("@edited_by_display",_actor.AuditLabel);
                await historyCmd.ExecuteNonQueryAsync();

                var updateCmd = connection.CreateCommand();
                updateCmd.Transaction = tx;
                updateCmd.CommandText = "UPDATE activity_log SET activity_type=@type, is_meaningful=@is_meaningful, occurred_at=@occurred_at, notes=@notes WHERE id=@id";
                updateCmd.Parameters.AddWithValue("@type", newType);
                updateCmd.Parameters.AddWithValue("@is_meaningful", MeaningfulActivityTypes.Contains(newType) ? 1 : 0);
                updateCmd.Parameters.AddWithValue("@occurred_at", newOccurredAt);
                updateCmd.Parameters.AddWithValue("@notes", DbValue(newNotes));
                updateCmd.Parameters.AddWithValue("@id", activityId);
                await updateCmd.ExecuteNonQueryAsync();

                var recomputeCmd = connection.CreateCommand();
                recomputeCmd.Transaction = tx;
                recomputeCmd.CommandText = """
                    UPDATE cases SET last_meaningful_activity_date =
                        (SELECT MAX(occurred_at) FROM activity_log WHERE case_id=@case_id AND is_meaningful=1)
                    WHERE id=@case_id
                    """;
                recomputeCmd.Parameters.AddWithValue("@case_id", caseId);
                await recomputeCmd.ExecuteNonQueryAsync();

                await SetAppSettingAsync(connection, tx, "last_save_result", $"Edited activity entry {activityId} at {DateTime.Now:G}");
            }

            return new ActivityLogEntry
            {
                Id = activityId,
                CaseId = caseId,
                ActivityType = newType,
                IsMeaningful = MeaningfulActivityTypes.Contains(newType),
                OccurredAt = newOccurredAt,
                Notes = newNotes,
                CreatedAt = now,ActorUserId=_actor.UserId?.ToString("D"),ActorDisplay=_actor.AuditLabel,
            };
        });
    }

    // --- Attorney Dashboard: quick-action single-field patches ---
    // Deliberately targeted UPDATEs, not a round trip through SaveCaseAsync (which also
    // regenerates checklist/deadlines) - a quick action should be fast and side-effect-free
    // beyond the one field(s) it's changing.

    private async Task<int> ExecuteCaseUpdateAsync(long caseId, string setClause, Action<SqliteCommand> bind)
    {
        return await WithWriteAsync(async (connection, tx) =>
        {
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"UPDATE cases SET {setClause}, updated_at=@updated_at WHERE id=@id";
            cmd.Parameters.AddWithValue("@updated_at", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("@id", caseId);
            bind(cmd);
            return await cmd.ExecuteNonQueryAsync();
        });
    }

    public async Task SetNextActionAsync(long caseId, SetNextActionRequest request)
    {
        await ExecuteCaseUpdateAsync(caseId, "next_action=@next_action, next_review_date=@next_review_date, next_action_due=@next_review_date", cmd =>
        {
            cmd.Parameters.AddWithValue("@next_action", DbValue(request.NextAction));
            cmd.Parameters.AddWithValue("@next_review_date", DbValue(request.NextReviewDate));
        });
        await RecordActivityAsync(caseId, "NextActionSet", request.NextAction, null);
    }

    public async Task SetWaitingAsync(long caseId, SetWaitingRequest request)
    {
        await ExecuteCaseUpdateAsync(caseId,
            "waiting_on=@waiting_on, waiting_reason=@waiting_reason, waiting_started_date=@waiting_started_date, " +
            "expected_response=@expected_response, waiting_follow_up_date=@waiting_follow_up_date, waiting_escalation_action=@waiting_escalation_action",
            cmd =>
            {
                cmd.Parameters.AddWithValue("@waiting_on", request.WaitingOn ?? "");
                cmd.Parameters.AddWithValue("@waiting_reason", DbValue(request.WaitingReason));
                cmd.Parameters.AddWithValue("@waiting_started_date", DbValue(request.WaitingStartedDate));
                cmd.Parameters.AddWithValue("@expected_response", DbValue(request.ExpectedResponse));
                cmd.Parameters.AddWithValue("@waiting_follow_up_date", request.WaitingFollowUpDate ?? "");
                cmd.Parameters.AddWithValue("@waiting_escalation_action", DbValue(request.WaitingEscalationAction));
            });
        await RecordActivityAsync(caseId, "MarkedWaiting", $"Waiting on {request.WaitingOn}", null);
    }

    // "Clearing a waiting condition" requires confirmation on the frontend (per the dashboard
    // brief) - this is the backend half, a plain clear of all waiting_* columns.
    public Task ClearWaitingAsync(long caseId) =>
        ExecuteCaseUpdateAsync(caseId,
            "waiting_on=NULL, waiting_reason=NULL, waiting_started_date=NULL, expected_response=NULL, " +
            "waiting_follow_up_date=NULL, waiting_escalation_action=NULL",
            _ => { });

    // A deferral is a documented reason + a mandatory future review date - the dashboard brief
    // is explicit that an alert can never be dismissed indefinitely, only deferred with both.
    public async Task DeferActionAsync(long caseId, DeferActionRequest request)
    {
        // Reason is optional (bulk defer is date-only; the per-case form still offers one).
        if (!DateOnly.TryParse(request.FutureReviewDate, out var until))
            throw new InvalidOperationException("A valid defer-until date is required.");
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        var now = DateTime.UtcNow.ToString("O");
        await ExecuteCaseUpdateAsync(caseId, "deferred_until=@until, deferred_reason=@reason, deferred_at=@at, deferred_by=@by", cmd =>
        {
            cmd.Parameters.AddWithValue("@until", until.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@reason", DbValue(reason));
            cmd.Parameters.AddWithValue("@at", now);
            cmd.Parameters.AddWithValue("@by", _actor.AuditLabel);
        });
        var detail = reason is null ? $"Deferred until {until:yyyy-MM-dd}" : $"Deferred until {until:yyyy-MM-dd}: {reason}";
        await RecordActivityAsync(caseId, "CaseDeferred", detail, null);
    }

    public async Task ClearDefermentAsync(long caseId, string? reason)
    {
        await ExecuteCaseUpdateAsync(caseId,
            "deferred_until=NULL, deferred_reason=NULL, deferred_at=NULL, deferred_by=NULL", _ => { });
        await RecordActivityAsync(caseId, "CaseDefermentCleared",
            string.IsNullOrWhiteSpace(reason) ? "Deferment cleared" : $"Deferment cleared: {reason.Trim()}", null);
    }

    // Applies the same deferral (reason + mandatory future review date) to a batch of cases at
    // once, so a flagged-for-attention sweep on the dashboard doesn't require opening each case.
    public async Task BulkDeferActionAsync(IReadOnlyList<long> caseIds, DeferActionRequest request)
    {
        foreach (var caseId in caseIds)
        {
            await DeferActionAsync(caseId, request);
        }
    }

    public async Task SetHolderAsync(long caseId, SetHolderRequest request)
    {
        await ExecuteCaseUpdateAsync(caseId, "current_holder=@current_holder, date_sent_to_current_holder=@date_sent",
            cmd =>
            {
                cmd.Parameters.AddWithValue("@current_holder", DbValue(request.CurrentHolder));
                cmd.Parameters.AddWithValue("@date_sent", DateTime.UtcNow.ToString("yyyy-MM-dd"));
            });
        await RecordActivityAsync(caseId, "HolderAssigned", $"Assigned to {request.CurrentHolder}", null);
    }

    public Task SetPriorityAsync(long caseId, SetPriorityRequest request) =>
        ExecuteCaseUpdateAsync(caseId, "priority=@priority", cmd => cmd.Parameters.AddWithValue("@priority", string.IsNullOrWhiteSpace(request.Priority) ? "Normal" : request.Priority));

    public Task SetTrialTrackAsync(long caseId, SetTrialTrackRequest request) =>
        ExecuteCaseUpdateAsync(caseId, "trial_track=@trial_track", cmd => cmd.Parameters.AddWithValue("@trial_track", request.TrialTrack ? 1 : 0));

    public async Task SetShortNoteAsync(long caseId, string note)
    {
        await ExecuteCaseUpdateAsync(caseId, "short_posture_summary=@note", cmd => cmd.Parameters.AddWithValue("@note", DbValue(note)));
        await RecordActivityAsync(caseId, "ShortNoteAdded", note, null);
    }

    public async Task<List<ComparableSaleRecord>> GetComparableSalesAsync(long? caseId)
    {
        var list = new List<ComparableSaleRecord>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, case_id, side, sale_description, sale_price, sale_date, size_acres, adjustment_notes, notes
            FROM comparable_sales
            WHERE (@caseId IS NULL OR case_id = @caseId)
            ORDER BY side, COALESCE(sale_date, '9999-12-31')
            """;
        cmd.Parameters.AddWithValue("@caseId", caseId is null ? DBNull.Value : caseId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new ComparableSaleRecord
            {
                Id = reader.GetInt64(0),
                CaseId = reader.GetInt64(1),
                Side = reader.GetString(2),
                SaleDescription = reader.IsDBNull(3) ? null : reader.GetString(3),
                SalePrice = reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                SaleDate = NormalizeDate(reader.IsDBNull(5) ? null : reader.GetString(5)),
                SizeAcres = reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                AdjustmentNotes = reader.IsDBNull(7) ? null : reader.GetString(7),
                Notes = reader.IsDBNull(8) ? null : reader.GetString(8)
            });
        }

        return list;
    }

    public async Task<ComparableSaleRecord> SaveComparableSaleAsync(ComparableSaleRecord model)
    {
        return await WithWriteAsync(async (connection, tx) =>
        {
            var now = DateTime.UtcNow.ToString("O");
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            if (model.Id == 0)
            {
                cmd.CommandText = """
                    INSERT INTO comparable_sales (case_id, side, sale_description, sale_price, sale_date, size_acres, adjustment_notes, notes, created_at, updated_at)
                    VALUES (@case_id, @side, @sale_description, @sale_price, @sale_date, @size_acres, @adjustment_notes, @notes, @now, @now);
                    SELECT last_insert_rowid();
                    """;
            }
            else
            {
                cmd.CommandText = """
                    UPDATE comparable_sales
                    SET side=@side, sale_description=@sale_description, sale_price=@sale_price, sale_date=@sale_date,
                        size_acres=@size_acres, adjustment_notes=@adjustment_notes, notes=@notes, updated_at=@now
                    WHERE id=@id;
                    SELECT @id;
                    """;
                cmd.Parameters.AddWithValue("@id", model.Id);
            }

            cmd.Parameters.AddWithValue("@case_id", model.CaseId);
            cmd.Parameters.AddWithValue("@side", model.Side);
            cmd.Parameters.AddWithValue("@sale_description", DbValue(model.SaleDescription));
            cmd.Parameters.AddWithValue("@sale_price", model.SalePrice.HasValue ? model.SalePrice.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@sale_date", DbValue(model.SaleDate));
            cmd.Parameters.AddWithValue("@size_acres", model.SizeAcres.HasValue ? model.SizeAcres.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@adjustment_notes", DbValue(model.AdjustmentNotes));
            cmd.Parameters.AddWithValue("@notes", DbValue(model.Notes));
            cmd.Parameters.AddWithValue("@now", now);
            model.Id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Saved comparable sale at {DateTime.Now:G}");
            return model;
        });
    }

    public async Task DeleteComparableSaleAsync(long id)
    {
        await WithWriteAsync(async (connection, tx) =>
        {
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM comparable_sales WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Deleted comparable sale {id} at {DateTime.Now:G}");
            return 0;
        });
    }

    public async Task<List<WitnessRecord>> GetWitnessesAsync(long? caseId)
    {
        var list = new List<WitnessRecord>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, case_id, name, side, role, contact_info, subpoena_status, outline_notes, notes, person_id
            FROM witnesses
            WHERE (@caseId IS NULL OR case_id = @caseId)
            ORDER BY side, name
            """;
        cmd.Parameters.AddWithValue("@caseId", caseId is null ? DBNull.Value : caseId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new WitnessRecord
            {
                Id = reader.GetInt64(0),
                CaseId = reader.GetInt64(1),
                Name = reader.GetString(2),
                Side = reader.GetString(3),
                Role = reader.IsDBNull(4) ? null : reader.GetString(4),
                ContactInfo = reader.IsDBNull(5) ? null : reader.GetString(5),
                SubpoenaStatus = reader.IsDBNull(6) ? "Not Needed" : reader.GetString(6),
                OutlineNotes = reader.IsDBNull(7) ? null : reader.GetString(7),
                Notes = reader.IsDBNull(8) ? null : reader.GetString(8),
                PersonId = reader.IsDBNull(9) ? null : reader.GetInt64(9)
            });
        }

        return list;
    }

    public async Task<WitnessRecord> SaveWitnessAsync(WitnessRecord model)
    {
        return await WithWriteAsync(async (connection, tx) =>
        {
            var now = DateTime.UtcNow.ToString("O");
            var isNew = model.Id == 0;

            // Multi-user rollout Phase 3: resolve the shared witness_persons identity for a new
            // witness row. If the client already resolved a person (the user picked a suggestion
            // from the registry search), use it as-is. Otherwise, only auto-link on an EXACT
            // normalized-name match to an existing person (the "just kept typing the right name"
            // case) - never auto-link on a merely "similar" name, that's a suggestion for the user
            // to accept or decline, not something the server should decide unattended. If neither
            // applies, create a brand-new witness_persons row.
            if (isNew)
            {
                model.PersonId = await ResolveOrCreateWitnessPersonAsync(connection, tx, model.PersonId, model.Name, model.ContactInfo, now);
            }

            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            if (isNew)
            {
                cmd.CommandText = """
                    INSERT INTO witnesses (case_id, name, side, role, contact_info, subpoena_status, outline_notes, notes, person_id, created_at, updated_at)
                    VALUES (@case_id, @name, @side, @role, @contact_info, @subpoena_status, @outline_notes, @notes, @person_id, @now, @now);
                    SELECT last_insert_rowid();
                    """;
            }
            else
            {
                cmd.CommandText = """
                    UPDATE witnesses
                    SET name=@name, side=@side, role=@role, contact_info=@contact_info, subpoena_status=@subpoena_status,
                        outline_notes=@outline_notes, notes=@notes, person_id=COALESCE(@person_id, person_id), updated_at=@now
                    WHERE id=@id;
                    SELECT @id;
                    """;
                cmd.Parameters.AddWithValue("@id", model.Id);
            }

            cmd.Parameters.AddWithValue("@case_id", model.CaseId);
            cmd.Parameters.AddWithValue("@name", model.Name);
            cmd.Parameters.AddWithValue("@side", model.Side);
            cmd.Parameters.AddWithValue("@role", DbValue(model.Role));
            cmd.Parameters.AddWithValue("@contact_info", DbValue(model.ContactInfo));
            cmd.Parameters.AddWithValue("@subpoena_status", model.SubpoenaStatus);
            cmd.Parameters.AddWithValue("@outline_notes", DbValue(model.OutlineNotes));
            cmd.Parameters.AddWithValue("@notes", DbValue(model.Notes));
            cmd.Parameters.AddWithValue("@person_id", model.PersonId is null ? DBNull.Value : model.PersonId);
            cmd.Parameters.AddWithValue("@now", now);
            model.Id = Convert.ToInt64(await cmd.ExecuteScalarAsync());

            if (!isNew)
            {
                // The update above only ever WIDENS person_id via COALESCE (never clears it), so
                // re-read the row's actual current value rather than trusting whatever the caller
                // happened to send (typically null, since editing an existing witness doesn't
                // resend its link) - otherwise the returned model would misreport an existing
                // link as gone even though the database still has it.
                var personLookup = connection.CreateCommand();
                personLookup.Transaction = tx;
                personLookup.CommandText = "SELECT person_id FROM witnesses WHERE id = @id";
                personLookup.Parameters.AddWithValue("@id", model.Id);
                var personResult = await personLookup.ExecuteScalarAsync();
                model.PersonId = personResult is null or DBNull ? null : Convert.ToInt64(personResult);
            }

            await SetAppSettingAsync(connection, tx, "last_save_result", $"Saved witness {model.Name} at {DateTime.Now:G}");
            return model;
        });
    }

    // Multi-user rollout Phase 3: shared helper for SaveWitnessAsync's link-or-create step.
    // - explicitPersonId (the client resolved a suggestion): used as-is, no lookup.
    // - otherwise: exact normalized-name match against existing witness_persons -> link to it.
    // - otherwise: create a new witness_persons row and link to that.
    private async Task<long> ResolveOrCreateWitnessPersonAsync(SqliteConnection connection, SqliteTransaction tx, long? explicitPersonId, string name, string? contactInfo, string now)
    {
        if (explicitPersonId is > 0)
        {
            return explicitPersonId.Value;
        }

        var normalized = WitnessNameMatcher.Normalize(name);
        if (normalized.Length > 0)
        {
            var findCmd = connection.CreateCommand();
            findCmd.Transaction = tx;
            findCmd.CommandText = "SELECT id FROM witness_persons WHERE LOWER(TRIM(name)) = @normalized LIMIT 1";
            findCmd.Parameters.AddWithValue("@normalized", normalized);
            var existing = await findCmd.ExecuteScalarAsync();
            if (existing is not null)
            {
                return Convert.ToInt64(existing);
            }
        }

        var insertCmd = connection.CreateCommand();
        insertCmd.Transaction = tx;
        insertCmd.CommandText = """
            INSERT INTO witness_persons (name, contact_info, created_at, updated_at)
            VALUES (@name, @contact, @now, @now);
            SELECT last_insert_rowid();
            """;
        insertCmd.Parameters.AddWithValue("@name", name);
        insertCmd.Parameters.AddWithValue("@contact", DbValue(contactInfo));
        insertCmd.Parameters.AddWithValue("@now", now);
        return Convert.ToInt64(await insertCmd.ExecuteScalarAsync());
    }

    // Multi-user rollout Phase 3: the witness registry search/autofill endpoint's backing query.
    // Blank query -> a plain, uncapped-by-similarity listing (ordered by name, capped generously)
    // for a full/paged list. Non-blank query -> ranked exact matches first (equals/starts-with/
    // contains - the common "just continuing to type the right name" case), then names flagged
    // similar by WitnessNameMatcher (nickname/prefix or small edit distance), capped to 10.
    public async Task<List<WitnessPersonMatch>> SearchWitnessPersonsAsync(string? query)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        var people = new List<(long Id, string Name, string? ContactInfo)>();
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, contact_info FROM witness_persons ORDER BY name";
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                people.Add((reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
            }
        }

        var normalizedQuery = WitnessNameMatcher.Normalize(query);
        List<(long Id, string Name, string? ContactInfo, string MatchType, int Rank)> ranked;
        if (normalizedQuery.Length == 0)
        {
            ranked = people.Select(p => (p.Id, p.Name, p.ContactInfo, MatchType: "exact", Rank: 0)).ToList();
        }
        else
        {
            ranked = [];
            foreach (var person in people)
            {
                var normalizedName = WitnessNameMatcher.Normalize(person.Name);
                if (normalizedName == normalizedQuery)
                {
                    ranked.Add((person.Id, person.Name, person.ContactInfo, "exact", 0));
                }
                else if (normalizedName.StartsWith(normalizedQuery, StringComparison.Ordinal))
                {
                    ranked.Add((person.Id, person.Name, person.ContactInfo, "exact", 1));
                }
                else if (normalizedName.Contains(normalizedQuery, StringComparison.Ordinal))
                {
                    ranked.Add((person.Id, person.Name, person.ContactInfo, "exact", 2));
                }
                else if (WitnessNameMatcher.AreSimilar(normalizedQuery, normalizedName))
                {
                    ranked.Add((person.Id, person.Name, person.ContactInfo, "similar", 3));
                }
            }
        }

        var capped = ranked.OrderBy(r => r.Rank).ThenBy(r => r.Name).Take(normalizedQuery.Length == 0 ? 200 : 10).ToList();
        if (capped.Count == 0)
        {
            return [];
        }

        // Cheap join for "which other case(s) is this person already a witness in" - one query
        // for all matched people rather than N+1.
        var caseNamesByPerson = new Dictionary<long, List<string>>();
        var personIdList = string.Join(",", capped.Select(c => c.Id));
        var caseCmd = connection.CreateCommand();
        caseCmd.CommandText = $"""
            SELECT w.person_id, c.case_number
            FROM witnesses w
            JOIN cases c ON c.id = w.case_id
            WHERE w.person_id IN ({personIdList})
            """;
        await using (var caseReader = await caseCmd.ExecuteReaderAsync())
        {
            while (await caseReader.ReadAsync())
            {
                var personId = caseReader.GetInt64(0);
                var caseNumber = caseReader.IsDBNull(1) ? null : caseReader.GetString(1);
                if (string.IsNullOrWhiteSpace(caseNumber))
                {
                    continue;
                }

                if (!caseNamesByPerson.TryGetValue(personId, out var caseList))
                {
                    caseList = [];
                    caseNamesByPerson[personId] = caseList;
                }

                if (!caseList.Contains(caseNumber))
                {
                    caseList.Add(caseNumber);
                }
            }
        }

        return capped.Select(r => new WitnessPersonMatch
        {
            Id = r.Id,
            Name = r.Name,
            ContactInfo = r.ContactInfo,
            MatchType = r.MatchType,
            OtherCaseNumbers = caseNamesByPerson.TryGetValue(r.Id, out var cases) ? cases : []
        }).ToList();
    }

    public async Task DeleteWitnessAsync(long id)
    {
        await WithWriteAsync(async (connection, tx) =>
        {
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM witnesses WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Deleted witness {id} at {DateTime.Now:G}");
            return 0;
        });
    }

    public async Task<List<ExhibitRecord>> GetExhibitsAsync(long? caseId)
    {
        var list = new List<ExhibitRecord>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, case_id, label, side, description, status, notes
            FROM exhibits
            WHERE (@caseId IS NULL OR case_id = @caseId)
            ORDER BY side, label
            """;
        cmd.Parameters.AddWithValue("@caseId", caseId is null ? DBNull.Value : caseId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new ExhibitRecord
            {
                Id = reader.GetInt64(0),
                CaseId = reader.GetInt64(1),
                Label = reader.GetString(2),
                Side = reader.GetString(3),
                Description = reader.IsDBNull(4) ? null : reader.GetString(4),
                Status = reader.IsDBNull(5) ? "Pre-Labeled" : reader.GetString(5),
                Notes = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }

        return list;
    }

    public async Task<ExhibitRecord> SaveExhibitAsync(ExhibitRecord model)
    {
        return await WithWriteAsync(async (connection, tx) =>
        {
            var now = DateTime.UtcNow.ToString("O");
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            if (model.Id == 0)
            {
                cmd.CommandText = """
                    INSERT INTO exhibits (case_id, label, side, description, status, notes, created_at, updated_at)
                    VALUES (@case_id, @label, @side, @description, @status, @notes, @now, @now);
                    SELECT last_insert_rowid();
                    """;
            }
            else
            {
                cmd.CommandText = """
                    UPDATE exhibits
                    SET label=@label, side=@side, description=@description, status=@status, notes=@notes, updated_at=@now
                    WHERE id=@id;
                    SELECT @id;
                    """;
                cmd.Parameters.AddWithValue("@id", model.Id);
            }

            cmd.Parameters.AddWithValue("@case_id", model.CaseId);
            cmd.Parameters.AddWithValue("@label", model.Label);
            cmd.Parameters.AddWithValue("@side", model.Side);
            cmd.Parameters.AddWithValue("@description", DbValue(model.Description));
            cmd.Parameters.AddWithValue("@status", model.Status);
            cmd.Parameters.AddWithValue("@notes", DbValue(model.Notes));
            cmd.Parameters.AddWithValue("@now", now);
            model.Id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Saved exhibit {model.Label} at {DateTime.Now:G}");
            return model;
        });
    }

    public async Task DeleteExhibitAsync(long id)
    {
        await WithWriteAsync(async (connection, tx) =>
        {
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM exhibits WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Deleted exhibit {id} at {DateTime.Now:G}");
            return 0;
        });
    }

    public async Task<List<TrialMotionRecord>> GetTrialMotionsAsync(long? caseId)
    {
        var list = new List<TrialMotionRecord>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, case_id, title, filed_by, filed_date, status, notes
            FROM trial_motions
            WHERE (@caseId IS NULL OR case_id = @caseId)
            ORDER BY COALESCE(filed_date, '9999-12-31')
            """;
        cmd.Parameters.AddWithValue("@caseId", caseId is null ? DBNull.Value : caseId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new TrialMotionRecord
            {
                Id = reader.GetInt64(0),
                CaseId = reader.GetInt64(1),
                Title = reader.GetString(2),
                FiledBy = reader.GetString(3),
                FiledDate = NormalizeDate(reader.IsDBNull(4) ? null : reader.GetString(4)),
                Status = reader.IsDBNull(5) ? "Pending" : reader.GetString(5),
                Notes = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }

        return list;
    }

    public async Task<TrialMotionRecord> SaveTrialMotionAsync(TrialMotionRecord model)
    {
        return await WithWriteAsync(async (connection, tx) =>
        {
            var now = DateTime.UtcNow.ToString("O");
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            if (model.Id == 0)
            {
                cmd.CommandText = """
                    INSERT INTO trial_motions (case_id, title, filed_by, filed_date, status, notes, created_at, updated_at)
                    VALUES (@case_id, @title, @filed_by, @filed_date, @status, @notes, @now, @now);
                    SELECT last_insert_rowid();
                    """;
            }
            else
            {
                cmd.CommandText = """
                    UPDATE trial_motions
                    SET title=@title, filed_by=@filed_by, filed_date=@filed_date, status=@status, notes=@notes, updated_at=@now
                    WHERE id=@id;
                    SELECT @id;
                    """;
                cmd.Parameters.AddWithValue("@id", model.Id);
            }

            cmd.Parameters.AddWithValue("@case_id", model.CaseId);
            cmd.Parameters.AddWithValue("@title", model.Title);
            cmd.Parameters.AddWithValue("@filed_by", model.FiledBy);
            cmd.Parameters.AddWithValue("@filed_date", DbValue(model.FiledDate));
            cmd.Parameters.AddWithValue("@status", model.Status);
            cmd.Parameters.AddWithValue("@notes", DbValue(model.Notes));
            cmd.Parameters.AddWithValue("@now", now);
            model.Id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Saved trial motion {model.Title} at {DateTime.Now:G}");
            return model;
        });
    }

    public async Task DeleteTrialMotionAsync(long id)
    {
        await WithWriteAsync(async (connection, tx) =>
        {
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM trial_motions WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Deleted trial motion {id} at {DateTime.Now:G}");
            return 0;
        });
    }

    public async Task<List<PublicationEntryRecord>> GetPublicationEntriesAsync(long? caseId)
    {
        var list = new List<PublicationEntryRecord>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, case_id, publication_number, publication_date, newspaper, proof_filed, proof_filed_date, service_resolved, notes
            FROM publication_dates
            WHERE (@caseId IS NULL OR case_id = @caseId)
            ORDER BY COALESCE(publication_date, '9999-12-31')
            """;
        cmd.Parameters.AddWithValue("@caseId", caseId is null ? DBNull.Value : caseId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new PublicationEntryRecord
            {
                Id = reader.GetInt64(0),
                CaseId = reader.GetInt64(1),
                PublicationNumber = reader.IsDBNull(2) ? "" : reader.GetString(2),
                PublicationDate = NormalizeDate(reader.IsDBNull(3) ? null : reader.GetString(3)),
                Newspaper = reader.IsDBNull(4) ? null : reader.GetString(4),
                ProofFiled = !reader.IsDBNull(5) && reader.GetInt64(5) == 1,
                ProofFiledDate = NormalizeDate(reader.IsDBNull(6) ? null : reader.GetString(6)),
                ServiceResolved = !reader.IsDBNull(7) && reader.GetInt64(7) == 1,
                Notes = reader.IsDBNull(8) ? null : reader.GetString(8)
            });
        }

        return list;
    }

    public async Task<PublicationEntryRecord> SavePublicationEntryAsync(PublicationEntryRecord model)
    {
        return await WithWriteAsync(async (connection, tx) =>
        {
            var now = DateTime.UtcNow.ToString("O");
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            if (model.Id == 0)
            {
                cmd.CommandText = """
                    INSERT INTO publication_dates (
                        case_id, publication_number, publication_date, newspaper, proof_filed,
                        proof_filed_date, service_resolved, notes, created_at, updated_at
                    )
                    VALUES (
                        @case_id, @publication_number, @publication_date, @newspaper, @proof_filed,
                        @proof_filed_date, @service_resolved, @notes, @created_at, @updated_at
                    );
                    SELECT last_insert_rowid();
                    """;
            }
            else
            {
                cmd.CommandText = """
                    UPDATE publication_dates
                    SET publication_number=@publication_number, publication_date=@publication_date, newspaper=@newspaper,
                        proof_filed=@proof_filed, proof_filed_date=@proof_filed_date,
                        service_resolved=@service_resolved, notes=@notes, updated_at=@updated_at
                    WHERE id=@id;
                    SELECT @id;
                    """;
                cmd.Parameters.AddWithValue("@id", model.Id);
            }

            cmd.Parameters.AddWithValue("@case_id", model.CaseId);
            cmd.Parameters.AddWithValue("@publication_number", model.PublicationNumber);
            cmd.Parameters.AddWithValue("@publication_date", DbValue(model.PublicationDate));
            cmd.Parameters.AddWithValue("@newspaper", DbValue(model.Newspaper));
            cmd.Parameters.AddWithValue("@proof_filed", model.ProofFiled ? 1 : 0);
            cmd.Parameters.AddWithValue("@proof_filed_date", DbValue(model.ProofFiledDate));
            cmd.Parameters.AddWithValue("@service_resolved", model.ServiceResolved ? 1 : 0);
            cmd.Parameters.AddWithValue("@notes", DbValue(model.Notes));
            cmd.Parameters.AddWithValue("@created_at", now);
            cmd.Parameters.AddWithValue("@updated_at", now);
            model.Id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Saved publication entry {model.PublicationNumber} at {DateTime.Now:G}");
            return model;
        });
    }

    public async Task DeletePublicationEntryAsync(long id)
    {
        await WithWriteAsync(async (connection, tx) =>
        {
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM publication_dates WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
            return 0;
        });
    }

    public async Task<List<OpposingAttorneyRecord>> GetOpposingAttorneysAsync(long? caseId)
    {
        var list = new List<OpposingAttorneyRecord>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, case_id, name, sort_order
            FROM case_opposing_attorneys
            WHERE (@caseId IS NULL OR case_id = @caseId)
            ORDER BY sort_order, id
            """;
        cmd.Parameters.AddWithValue("@caseId", caseId is null ? DBNull.Value : caseId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new OpposingAttorneyRecord
            {
                Id = reader.GetInt64(0),
                CaseId = reader.GetInt64(1),
                Name = reader.IsDBNull(2) ? "" : reader.GetString(2),
                SortOrder = reader.IsDBNull(3) ? 0 : reader.GetInt32(3)
            });
        }

        return list;
    }

    public async Task<OpposingAttorneyRecord> SaveOpposingAttorneyAsync(OpposingAttorneyRecord model)
    {
        return await WithWriteAsync(async (connection, tx) =>
        {
            var now = DateTime.UtcNow.ToString("O");
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            if (model.Id == 0)
            {
                var nextOrderCmd = connection.CreateCommand();
                nextOrderCmd.Transaction = tx;
                nextOrderCmd.CommandText = "SELECT COALESCE(MAX(sort_order), -1) + 1 FROM case_opposing_attorneys WHERE case_id=@case_id";
                nextOrderCmd.Parameters.AddWithValue("@case_id", model.CaseId);
                model.SortOrder = Convert.ToInt32(await nextOrderCmd.ExecuteScalarAsync());

                cmd.CommandText = """
                    INSERT INTO case_opposing_attorneys (case_id, name, sort_order, created_at, updated_at)
                    VALUES (@case_id, @name, @sort_order, @created_at, @updated_at);
                    SELECT last_insert_rowid();
                    """;
            }
            else
            {
                cmd.CommandText = """
                    UPDATE case_opposing_attorneys
                    SET name=@name, sort_order=@sort_order, updated_at=@updated_at
                    WHERE id=@id;
                    SELECT @id;
                    """;
                cmd.Parameters.AddWithValue("@id", model.Id);
            }

            cmd.Parameters.AddWithValue("@case_id", model.CaseId);
            cmd.Parameters.AddWithValue("@name", model.Name);
            cmd.Parameters.AddWithValue("@sort_order", model.SortOrder);
            cmd.Parameters.AddWithValue("@created_at", now);
            cmd.Parameters.AddWithValue("@updated_at", now);
            model.Id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Saved opposing attorney {model.Name} at {DateTime.Now:G}");
            return model;
        });
    }

    public async Task DeleteOpposingAttorneyAsync(long id)
    {
        await WithWriteAsync(async (connection, tx) =>
        {
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM case_opposing_attorneys WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Deleted opposing attorney {id} at {DateTime.Now:G}");
            return 0;
        });
    }

    public async Task<List<IssueTagRecord>> GetIssueTagsAsync()
    {
        var list = new List<IssueTagRecord>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, description, category FROM issue_tags WHERE is_deleted = 0 ORDER BY category, name";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new IssueTagRecord
            {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1),
                Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                Category = reader.IsDBNull(3) ? null : reader.GetString(3)
            });
        }

        return list;
    }

    public async Task<List<CaseIssueTagRecord>> GetCaseIssueTagsAsync(long caseId)
    {
        var list = new List<CaseIssueTagRecord>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT cit.id, cit.case_id, cit.issue_tag_id, it.name, it.category, it.description, cit.notes
            FROM case_issue_tags cit
            JOIN issue_tags it ON it.id = cit.issue_tag_id
            WHERE cit.case_id = @caseId
            ORDER BY it.category, it.name
            """;
        cmd.Parameters.AddWithValue("@caseId", caseId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new CaseIssueTagRecord
            {
                Id = reader.GetInt64(0),
                CaseId = reader.GetInt64(1),
                IssueTagId = reader.GetInt64(2),
                TagName = reader.GetString(3),
                Category = reader.IsDBNull(4) ? null : reader.GetString(4),
                Description = reader.IsDBNull(5) ? null : reader.GetString(5),
                Notes = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }

        return list;
    }

    public async Task AddIssueTagAsync(long caseId, long issueTagId)
    {
        await WithWriteAsync(async (connection, tx) =>
        {
            var check = connection.CreateCommand();
            check.Transaction = tx;
            check.CommandText = "SELECT COUNT(*) FROM case_issue_tags WHERE case_id=@caseId AND issue_tag_id=@tagId";
            check.Parameters.AddWithValue("@caseId", caseId);
            check.Parameters.AddWithValue("@tagId", issueTagId);
            if (Convert.ToInt32(await check.ExecuteScalarAsync()) > 0)
            {
                throw new DuplicateIssueTagException("This issue tag is already assigned to the case.");
            }

            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO case_issue_tags (case_id, issue_tag_id, notes, created_at, updated_at) VALUES (@caseId, @tagId, NULL, @now, @now)";
            cmd.Parameters.AddWithValue("@caseId", caseId);
            cmd.Parameters.AddWithValue("@tagId", issueTagId);
            cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
            var generated = await GenerateChecklistForCaseAsync(connection, tx, caseId);
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Added issue tag {issueTagId} at {DateTime.Now:G}; generated {generated} checklist item(s)");
            return 0;
        });
    }

    public async Task RemoveIssueTagAsync(long caseIssueTagId)
    {
        await WithWriteAsync(async (connection, tx) =>
        {
            long? caseId = null;
            string? tagName = null;
            var lookup = connection.CreateCommand();
            lookup.Transaction = tx;
            lookup.CommandText = """
                SELECT cit.case_id, it.name
                FROM case_issue_tags cit
                JOIN issue_tags it ON it.id = cit.issue_tag_id
                WHERE cit.id = @id
                """;
            lookup.Parameters.AddWithValue("@id", caseIssueTagId);
            await using (var reader = await lookup.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    caseId = reader.GetInt64(0);
                    tagName = reader.GetString(1);
                }
            }

            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM case_issue_tags WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", caseIssueTagId);
            await cmd.ExecuteNonQueryAsync();

            // Auto-mark this tag's generated, still-open items as N/A. Manual and completed items are never touched.
            if (caseId is not null && tagName is not null)
            {
                var na = connection.CreateCommand();
                na.Transaction = tx;
                na.CommandText = """
                    UPDATE checklist_items
                    SET status = 'N/A',
                        notes = CASE WHEN COALESCE(notes, '') = '' THEN @why ELSE notes || ' | ' || @why END,
                        updated_at = @now
                    WHERE case_id = @caseId
                      AND is_manual = 0
                      AND status NOT IN ('Done', 'Complete', 'N/A')
                      AND source_type IN (
                          SELECT 'Template:' || t.name || ':' || ti.sort_order
                          FROM checklist_template_items ti
                          JOIN checklist_templates t ON t.id = ti.template_id
                          WHERE t.trigger_type = 'IssueTag' AND t.issue_tag_name = @tagName
                      )
                    """;
                na.Parameters.AddWithValue("@caseId", caseId);
                na.Parameters.AddWithValue("@tagName", tagName);
                na.Parameters.AddWithValue("@why", $"Auto-marked N/A: issue tag '{tagName}' removed.");
                na.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
                await na.ExecuteNonQueryAsync();
            }

            await SetAppSettingAsync(connection, tx, "last_save_result", $"Removed issue tag at {DateTime.Now:G}");
            return 0;
        });
    }

    public async Task<List<DocumentExportRecord>> GetDocumentExportsAsync(long? caseId)
    {
        var list = new List<DocumentExportRecord>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, case_id, document_type, document_title, output_path, created_at, status, qa_status, qa_notes, error_message, content_text, base_template_version, issue_tag_versions, is_draft, is_finalized, merge_field_values, created_by_user_id, created_by_display, qa_reviewed_by_user_id, qa_reviewed_by_display
            FROM document_exports
            WHERE (@caseId IS NULL OR case_id = @caseId)
            ORDER BY created_at DESC
            """;
        cmd.Parameters.Add("@caseId", SqliteType.Integer).Value = (object?)caseId ?? DBNull.Value;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new DocumentExportRecord
            {
                Id = reader.GetInt64(0),
                CaseId = reader.GetInt64(1),
                DocumentType = reader.GetString(2),
                DocumentTitle = reader.GetString(3),
                OutputPath = reader.GetString(4),
                CreatedAt = reader.GetString(5),
                Status = reader.IsDBNull(6) ? "Generated" : reader.GetString(6),
                QaStatus = reader.IsDBNull(7) ? "Not Reviewed" : reader.GetString(7),
                QaNotes = reader.IsDBNull(8) ? null : reader.GetString(8),
                ErrorMessage = reader.IsDBNull(9) ? null : reader.GetString(9),
                ContentText = reader.IsDBNull(10) ? null : reader.GetString(10),
                BaseTemplateVersion = reader.IsDBNull(11) ? null : reader.GetString(11),
                IssueTagVersions = reader.IsDBNull(12) ? null : reader.GetString(12),
                IsDraft = !reader.IsDBNull(13) && reader.GetInt32(13) != 0,
                IsFinalized = reader.IsDBNull(14) || reader.GetInt32(14) != 0,
                MergeFieldValues = reader.IsDBNull(15) ? null : reader.GetString(15),
                CreatedByUserId=reader.IsDBNull(16)?null:reader.GetString(16),CreatedByDisplay=reader.IsDBNull(17)?null:reader.GetString(17),
                QaReviewedByUserId=reader.IsDBNull(18)?null:reader.GetString(18),QaReviewedByDisplay=reader.IsDBNull(19)?null:reader.GetString(19)
            });
        }

        return list;
    }

    public async Task<List<CaseNoteRecord>> GetCaseNotesAsync(long? caseId)
    {
        var list = new List<CaseNoteRecord>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, case_id, title, body, created_at, updated_at
            FROM case_notes
            WHERE (@caseId IS NULL OR case_id=@caseId)
            ORDER BY updated_at DESC, id DESC
            """;
        cmd.Parameters.AddWithValue("@caseId", caseId is null ? DBNull.Value : caseId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new CaseNoteRecord
            {
                Id = reader.GetInt64(0),
                CaseId = reader.GetInt64(1),
                Title = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Body = reader.IsDBNull(3) ? "" : reader.GetString(3),
                CreatedAt = reader.IsDBNull(4) ? "" : reader.GetString(4),
                UpdatedAt = reader.IsDBNull(5) ? "" : reader.GetString(5)
            });
        }

        return list;
    }

    public async Task<CaseNoteRecord> SaveCaseNoteAsync(CaseNoteRecord model)
    {
        return await WithWriteAsync(async (connection, tx) =>
        {
            var now = DateTime.UtcNow.ToString("O");
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            if (model.Id == 0)
            {
                cmd.CommandText = """
                    INSERT INTO case_notes (case_id, title, body, created_at, updated_at)
                    VALUES (@case_id, @title, @body, @created_at, @updated_at);
                    SELECT last_insert_rowid();
                    """;
                cmd.Parameters.AddWithValue("@created_at", now);
            }
            else
            {
                cmd.CommandText = """
                    UPDATE case_notes
                    SET title=@title, body=@body, updated_at=@updated_at
                    WHERE id=@id;
                    SELECT @id;
                    """;
                cmd.Parameters.AddWithValue("@id", model.Id);
            }

            cmd.Parameters.AddWithValue("@case_id", model.CaseId);
            cmd.Parameters.AddWithValue("@title", DbValue(BlankToNull(model.Title) ?? "Untitled Note"));
            cmd.Parameters.AddWithValue("@body", DbValue(model.Body));
            cmd.Parameters.AddWithValue("@updated_at", now);
            model.Id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            model.CreatedAt = string.IsNullOrWhiteSpace(model.CreatedAt) ? now : model.CreatedAt;
            model.UpdatedAt = now;
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Saved case note for case {model.CaseId} at {DateTime.Now:G}");
            return model;
        });
    }

    public async Task DeleteCaseNoteAsync(long id)
    {
        await WithWriteAsync(async (connection, tx) =>
        {
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM case_notes WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Deleted case note {id} at {DateTime.Now:G}");
            return 0;
        });
    }

    public async Task<List<HearingRecord>> GetHearingsAsync(long? caseId)
    {
        var list = new List<HearingRecord>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, case_id, title, hearing_date, location, description, created_at, updated_at, event_type, status
            FROM hearings
            WHERE (@caseId IS NULL OR case_id = @caseId)
            ORDER BY COALESCE(hearing_date, '9999-12-31') DESC, id DESC
            """;
        cmd.Parameters.AddWithValue("@caseId", caseId is null ? DBNull.Value : caseId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new HearingRecord
            {
                Id = reader.GetInt64(0),
                CaseId = reader.GetInt64(1),
                Title = reader.IsDBNull(2) ? "" : reader.GetString(2),
                HearingDate = NormalizeDate(reader.IsDBNull(3) ? null : reader.GetString(3)),
                Location = reader.IsDBNull(4) ? null : reader.GetString(4),
                Description = reader.IsDBNull(5) ? null : reader.GetString(5),
                CreatedAt = reader.IsDBNull(6) ? "" : reader.GetString(6),
                UpdatedAt = reader.IsDBNull(7) ? "" : reader.GetString(7),
                EventType = reader.IsDBNull(8) ? "Hearing" : reader.GetString(8),
                Status = reader.IsDBNull(9) ? "Scheduled" : reader.GetString(9),
            });
        }

        return list;
    }

    public async Task<HearingRecord> SaveHearingAsync(HearingRecord model)
    {
        return await WithWriteAsync(async (connection, tx) =>
        {
            var now = DateTime.UtcNow.ToString("O");
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            if (model.Id == 0)
            {
                cmd.CommandText = """
                    INSERT INTO hearings (case_id, title, hearing_date, location, description, created_at, updated_at, event_type, status)
                    VALUES (@case_id, @title, @hearing_date, @location, @description, @created_at, @updated_at, @event_type, @status);
                    SELECT last_insert_rowid();
                    """;
                cmd.Parameters.AddWithValue("@created_at", now);
            }
            else
            {
                cmd.CommandText = """
                    UPDATE hearings
                    SET title=@title, hearing_date=@hearing_date, location=@location, description=@description, updated_at=@updated_at, event_type=@event_type, status=@status
                    WHERE id=@id;
                    SELECT @id;
                    """;
                cmd.Parameters.AddWithValue("@id", model.Id);
            }

            cmd.Parameters.AddWithValue("@case_id", model.CaseId);
            cmd.Parameters.AddWithValue("@title", DbValue(BlankToNull(model.Title) ?? "Hearing"));
            cmd.Parameters.AddWithValue("@hearing_date", DbValue(model.HearingDate));
            cmd.Parameters.AddWithValue("@location", DbValue(model.Location));
            cmd.Parameters.AddWithValue("@description", DbValue(model.Description));
            cmd.Parameters.AddWithValue("@updated_at", now);
            cmd.Parameters.AddWithValue("@event_type", DbValue(BlankToNull(model.EventType) ?? "Hearing"));
            cmd.Parameters.AddWithValue("@status", DbValue(BlankToNull(model.Status) ?? "Scheduled"));
            model.Id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            model.CreatedAt = string.IsNullOrWhiteSpace(model.CreatedAt) ? now : model.CreatedAt;
            model.UpdatedAt = now;
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Saved hearing for case {model.CaseId} at {DateTime.Now:G}");
            return model;
        });
    }

    public async Task DeleteHearingAsync(long id)
    {
        await WithWriteAsync(async (connection, tx) =>
        {
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM hearings WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Deleted hearing {id} at {DateTime.Now:G}");
            return 0;
        });
    }

    public async Task<List<ChecklistTemplateRecord>> GetChecklistTemplatesAsync()
    {
        var templates = new List<ChecklistTemplateRecord>();
        var byId = new Dictionary<long, ChecklistTemplateRecord>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();

        var templateCmd = connection.CreateCommand();
        templateCmd.CommandText = """
            SELECT id, name, trigger_type, stage, issue_tag_name, track, active
            FROM checklist_templates
            ORDER BY name
            """;
        await using (var reader = await templateCmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var record = new ChecklistTemplateRecord
                {
                    Id = reader.GetInt64(0),
                    Name = reader.GetString(1),
                    TriggerType = reader.GetString(2),
                    Stage = reader.IsDBNull(3) ? null : reader.GetString(3),
                    IssueTagName = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Track = reader.IsDBNull(5) ? "Any" : reader.GetString(5),
                    Active = !reader.IsDBNull(6) && reader.GetInt64(6) == 1
                };
                templates.Add(record);
                byId[record.Id] = record;
            }
        }

        var itemCmd = connection.CreateCommand();
        itemCmd.CommandText = """
            SELECT id, template_id, task, phase, sort_order, due_offset_days
            FROM checklist_template_items
            ORDER BY template_id, sort_order, id
            """;
        await using (var reader = await itemCmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var templateId = reader.GetInt64(1);
                if (!byId.TryGetValue(templateId, out var template))
                {
                    continue;
                }

                template.Items.Add(new ChecklistTemplateItemRecord
                {
                    Id = reader.GetInt64(0),
                    TemplateId = templateId,
                    Task = reader.GetString(2),
                    Phase = reader.IsDBNull(3) ? null : reader.GetString(3),
                    SortOrder = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                    DueOffsetDays = reader.IsDBNull(5) ? null : reader.GetInt32(5)
                });
            }
        }

        return templates;
    }

    public async Task<ChecklistTemplateRecord> SaveChecklistTemplateAsync(ChecklistTemplateRecord model)
    {
        return await WithWriteAsync(async (connection, tx) =>
        {
            var now = DateTime.UtcNow.ToString("O");
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            if (model.Id == 0)
            {
                cmd.CommandText = """
                    INSERT INTO checklist_templates (name, trigger_type, stage, issue_tag_name, track, active, created_at, updated_at)
                    VALUES (@name, @trigger_type, @stage, @issue_tag_name, @track, @active, @created_at, @updated_at);
                    SELECT last_insert_rowid();
                    """;
                cmd.Parameters.AddWithValue("@created_at", now);
            }
            else
            {
                cmd.CommandText = """
                    UPDATE checklist_templates
                    SET name=@name, trigger_type=@trigger_type, stage=@stage, issue_tag_name=@issue_tag_name,
                        track=@track, active=@active, updated_at=@updated_at
                    WHERE id=@id;
                    SELECT @id;
                    """;
                cmd.Parameters.AddWithValue("@id", model.Id);
            }

            cmd.Parameters.AddWithValue("@name", model.Name.Trim());
            cmd.Parameters.AddWithValue("@trigger_type", model.TriggerType);
            cmd.Parameters.AddWithValue("@stage", DbValue(model.Stage));
            cmd.Parameters.AddWithValue("@issue_tag_name", DbValue(model.IssueTagName));
            cmd.Parameters.AddWithValue("@track", string.IsNullOrWhiteSpace(model.Track) ? "Any" : model.Track);
            cmd.Parameters.AddWithValue("@active", model.Active ? 1 : 0);
            cmd.Parameters.AddWithValue("@updated_at", now);
            model.Id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Saved checklist template '{model.Name}' at {DateTime.Now:G}");
            return model;
        });
    }

    public async Task DeleteChecklistTemplateAsync(long id)
    {
        await WithWriteAsync(async (connection, tx) =>
        {
            var deleteItems = connection.CreateCommand();
            deleteItems.Transaction = tx;
            deleteItems.CommandText = "DELETE FROM checklist_template_items WHERE template_id=@id";
            deleteItems.Parameters.AddWithValue("@id", id);
            await deleteItems.ExecuteNonQueryAsync();

            var deleteTemplate = connection.CreateCommand();
            deleteTemplate.Transaction = tx;
            deleteTemplate.CommandText = "DELETE FROM checklist_templates WHERE id=@id";
            deleteTemplate.Parameters.AddWithValue("@id", id);
            await deleteTemplate.ExecuteNonQueryAsync();
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Deleted checklist template {id} at {DateTime.Now:G}");
            return 0;
        });
    }

    public async Task<ChecklistTemplateItemRecord> SaveChecklistTemplateItemAsync(ChecklistTemplateItemRecord model)
    {
        return await WithWriteAsync(async (connection, tx) =>
        {
            var now = DateTime.UtcNow.ToString("O");
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            if (model.Id == 0)
            {
                cmd.CommandText = """
                    INSERT INTO checklist_template_items (template_id, task, phase, sort_order, due_offset_days, created_at, updated_at)
                    VALUES (@template_id, @task, @phase, @sort_order, @due_offset_days, @created_at, @updated_at);
                    SELECT last_insert_rowid();
                    """;
                cmd.Parameters.AddWithValue("@created_at", now);
            }
            else
            {
                cmd.CommandText = """
                    UPDATE checklist_template_items
                    SET task=@task, phase=@phase, sort_order=@sort_order, due_offset_days=@due_offset_days, updated_at=@updated_at
                    WHERE id=@id;
                    SELECT @id;
                    """;
                cmd.Parameters.AddWithValue("@id", model.Id);
            }

            cmd.Parameters.AddWithValue("@template_id", model.TemplateId);
            cmd.Parameters.AddWithValue("@task", model.Task.Trim());
            cmd.Parameters.AddWithValue("@phase", DbValue(model.Phase));
            cmd.Parameters.AddWithValue("@sort_order", model.SortOrder);
            cmd.Parameters.AddWithValue("@due_offset_days", model.DueOffsetDays.HasValue ? model.DueOffsetDays.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@updated_at", now);
            model.Id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Saved checklist template item {model.Id} at {DateTime.Now:G}");
            return model;
        });
    }

    public async Task DeleteChecklistTemplateItemAsync(long id)
    {
        await WithWriteAsync(async (connection, tx) =>
        {
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM checklist_template_items WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Deleted checklist template item {id} at {DateTime.Now:G}");
            return 0;
        });
    }

    public async Task<FileExportResult> ExportCaseNotesAsync(long caseId)
    {
        var workspace = await GetCaseWorkspaceAsync(caseId) ?? throw new InvalidOperationException("Case not found.");
        var notes = await GetCaseNotesAsync(caseId);
        var fileName = $"{SanitizeFileSegment(workspace.Case.CaseNumber)}_CaseNotes_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
        var outputPath = _documents.CreatePath(caseId,fileName);
        var lines = new List<string>
        {
            $"{workspace.Case.CaseName} ({workspace.Case.CaseNumber})",
            $"Generated: {DateTime.Now:G}",
            ""
        };

        if (notes.Count == 0)
        {
            lines.Add("No case notes yet.");
        }
        else
        {
            foreach (var note in notes)
            {
                lines.Add(note.Title);
                lines.Add($"Created: {DisplayTimestamp(note.CreatedAt)}");
                lines.Add($"Last Updated: {DisplayTimestamp(note.UpdatedAt)}");
                lines.Add(note.Body);
                lines.Add("");
            }
        }

        await _documents.WriteLinesAsync(outputPath,lines);
        return new FileExportResult
        {
            Title = $"{workspace.Case.CaseNumber} Case Notes",
            OutputPath = outputPath
        };
    }

    public async Task<DocumentExportRecord> GenerateDocumentAsync(long caseId, string kind)
    {
        var workspace = await GetCaseWorkspaceAsync(caseId) ?? throw new InvalidOperationException("Case not found.");
        var title = kind == "summary" ? "Case Summary" : "Case Review Memo";
        var slug = kind == "summary" ? "Case_Summary" : "Case_Review_Memo";
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        var outputPath = _documents.CreatePath(caseId,$"{stamp}_{SanitizeFileSegment(workspace.Case.CaseNumber)}_{slug}.txt");
        var content = BuildDocumentText(workspace, title);
        if (content.Contains("1900-01-01", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Generated content contains an invalid placeholder date.");
        }

        return await WithWriteAsync(async (connection, tx) =>
        {
            await _documents.WriteTextAsync(outputPath,content);
            var now = DateTime.UtcNow.ToString("O");
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO document_exports (case_id, document_type, document_title, output_path, created_at, status, qa_status, qa_notes, error_message, content_text, is_draft, is_finalized, created_by_user_id, created_by_display)
                VALUES (@caseId, @document_type, @document_title, @output_path, @created_at, 'Generated', 'Not Reviewed', NULL, NULL, @content_text, 0, 1, @created_by_user_id, @created_by_display);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("@caseId", caseId);
            cmd.Parameters.AddWithValue("@document_type", title);
            cmd.Parameters.AddWithValue("@document_title", title);
            cmd.Parameters.AddWithValue("@output_path", outputPath);
            cmd.Parameters.AddWithValue("@created_at", now);
            cmd.Parameters.AddWithValue("@content_text", content);
            cmd.Parameters.AddWithValue("@created_by_user_id",(object?)_actor.UserId?.ToString("D")??DBNull.Value);cmd.Parameters.AddWithValue("@created_by_display",_actor.AuditLabel);
            var id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            await SetAppSettingAsync(connection, tx, "last_document_generation_result", $"Generated {title} at {DateTime.Now:G}");
            return new DocumentExportRecord
            {
                Id = id,
                CaseId = caseId,
                DocumentType = title,
                DocumentTitle = title,
                OutputPath = outputPath,
                CreatedAt = now,
                Status = "Generated",
                QaStatus = "Not Reviewed", ContentText = content, IsFinalized = true,CreatedByUserId=_actor.UserId?.ToString("D"),CreatedByDisplay=_actor.AuditLabel
            };
        });
    }

    public async Task<DocumentExportRecord> SaveDocumentQaAsync(DocumentExportRecord model)
    {
        return await WithWriteAsync(async (connection, tx) =>
        {
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE document_exports SET qa_status=@qa_status, qa_notes=@qa_notes, qa_reviewed_by_user_id=@reviewed_by_user_id, qa_reviewed_by_display=@reviewed_by_display WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", model.Id);
            cmd.Parameters.AddWithValue("@qa_status", model.QaStatus);
            cmd.Parameters.AddWithValue("@qa_notes", DbValue(model.QaNotes));
            cmd.Parameters.AddWithValue("@reviewed_by_user_id",(object?)_actor.UserId?.ToString("D")??DBNull.Value);cmd.Parameters.AddWithValue("@reviewed_by_display",_actor.AuditLabel);
            await cmd.ExecuteNonQueryAsync();
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Saved QA notes for export {model.Id} at {DateTime.Now:G}");
            model.QaReviewedByUserId=_actor.UserId?.ToString("D");model.QaReviewedByDisplay=_actor.AuditLabel;return model;
        });
    }

    public async Task<OrgDefaults> GetOrgDefaultsAsync()
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var json = await GetAppSettingAsync(connection, "org_defaults_json");
        if (string.IsNullOrWhiteSpace(json))
        {
            return new OrgDefaults();
        }

        return JsonSerializer.Deserialize<OrgDefaults>(json) ?? new OrgDefaults();
    }

    public async Task<OrgDefaults> SaveOrgDefaultsAsync(OrgDefaults model)
    {
        return await WithWriteAsync(async (connection, tx) =>
        {
            var json = JsonSerializer.Serialize(model);
            await SetAppSettingAsync(connection, tx, "org_defaults_json", json);
            return model;
        });
    }

    public async Task<DocumentExportRecord?> GetDocumentExportByIdAsync(long id)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, case_id, document_type, document_title, output_path, created_at, status, qa_status, qa_notes, error_message, content_text, base_template_version, issue_tag_versions, is_draft, is_finalized, merge_field_values, created_by_user_id, created_by_display, qa_reviewed_by_user_id, qa_reviewed_by_display
            FROM document_exports WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("@id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new DocumentExportRecord
        {
            Id = reader.GetInt64(0),
            CaseId = reader.GetInt64(1),
            DocumentType = reader.GetString(2),
            DocumentTitle = reader.GetString(3),
            OutputPath = reader.GetString(4),
            CreatedAt = reader.GetString(5),
            Status = reader.IsDBNull(6) ? "Generated" : reader.GetString(6),
            QaStatus = reader.IsDBNull(7) ? "Not Reviewed" : reader.GetString(7),
            QaNotes = reader.IsDBNull(8) ? null : reader.GetString(8),
            ErrorMessage = reader.IsDBNull(9) ? null : reader.GetString(9),
            ContentText = reader.IsDBNull(10) ? null : reader.GetString(10),
            BaseTemplateVersion = reader.IsDBNull(11) ? null : reader.GetString(11),
            IssueTagVersions = reader.IsDBNull(12) ? null : reader.GetString(12),
            IsDraft = !reader.IsDBNull(13) && reader.GetInt32(13) != 0,
            IsFinalized = reader.IsDBNull(14) || reader.GetInt32(14) != 0,
            MergeFieldValues = reader.IsDBNull(15) ? null : reader.GetString(15),CreatedByUserId=reader.IsDBNull(16)?null:reader.GetString(16),CreatedByDisplay=reader.IsDBNull(17)?null:reader.GetString(17),QaReviewedByUserId=reader.IsDBNull(18)?null:reader.GetString(18),QaReviewedByDisplay=reader.IsDBNull(19)?null:reader.GetString(19)
        };
    }

    public async Task<bool> DeleteDocumentExportAsync(long id)
    {
        var outputPath = await WithWriteAsync(async (connection, tx) =>
        {
            var select = connection.CreateCommand();
            select.Transaction = tx;
            select.CommandText = "SELECT output_path FROM document_exports WHERE id = @id";
            select.Parameters.AddWithValue("@id", id);
            var result = await select.ExecuteScalarAsync();
            if (result is null)
            {
                return null;
            }

            var delete = connection.CreateCommand();
            delete.Transaction = tx;
            delete.CommandText = "DELETE FROM document_exports WHERE id = @id";
            delete.Parameters.AddWithValue("@id", id);
            await delete.ExecuteNonQueryAsync();
            return Convert.ToString(result);
        });
        if (outputPath is null)
        {
            return false;
        }

        await _documents.DeleteIfExistsAsync(outputPath);
        return true;
    }

    public async Task<DocumentExportRecord> SaveGeneratedBinaryDocumentAsync(long caseId, SaveGeneratedDocumentRequest request, byte[] content, string extension, CancellationToken token = default)
    {
        var workspace = await GetCaseWorkspaceAsync(caseId) ?? throw new InvalidOperationException("Case not found.");
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        var safeExtension = string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase) ? ".docx" : ".bin";
        var outputPath = _documents.CreatePath(caseId, $"{stamp}_{SanitizeFileSegment(workspace.Case.CaseNumber)}_{SanitizeFileSegment(request.Title)}{safeExtension}");
        return await WithWriteAsync(async (connection, tx) =>
        {
            await _documents.WriteBytesAsync(outputPath, content, token);
            var now = DateTime.UtcNow.ToString("O");
            var cmd = connection.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO document_exports (case_id, document_type, document_title, output_path, created_at, status, qa_status, qa_notes, error_message, content_text, base_template_version, issue_tag_versions, is_draft, is_finalized, merge_field_values, created_by_user_id, created_by_display)
                VALUES (@caseId, @document_type, @document_title, @output_path, @created_at, 'Generated', 'Not Reviewed', NULL, NULL, NULL, @base_template_version, @issue_tag_versions, @is_draft, @is_finalized, @merge_field_values, @created_by_user_id, @created_by_display);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("@caseId", caseId); cmd.Parameters.AddWithValue("@document_type", request.Kind); cmd.Parameters.AddWithValue("@document_title", request.Title);
            cmd.Parameters.AddWithValue("@output_path", outputPath); cmd.Parameters.AddWithValue("@created_at", now); cmd.Parameters.AddWithValue("@base_template_version", DbValue(request.BaseTemplateVersion));
            cmd.Parameters.AddWithValue("@issue_tag_versions", DbValue(request.IssueTagVersions)); cmd.Parameters.AddWithValue("@is_draft", request.IsDraft ? 1 : 0); cmd.Parameters.AddWithValue("@is_finalized", request.IsFinalized ? 1 : 0);
            cmd.Parameters.AddWithValue("@merge_field_values", DbValue(request.MergeFieldValues)); cmd.Parameters.AddWithValue("@created_by_user_id", (object?)_actor.UserId?.ToString("D") ?? DBNull.Value); cmd.Parameters.AddWithValue("@created_by_display", _actor.AuditLabel);
            var id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            return new DocumentExportRecord { Id = id, CaseId = caseId, DocumentType = request.Kind, DocumentTitle = request.Title, OutputPath = outputPath, CreatedAt = now, Status = "Generated", QaStatus = "Not Reviewed", BaseTemplateVersion = request.BaseTemplateVersion, IssueTagVersions = request.IssueTagVersions, MergeFieldValues = request.MergeFieldValues, IsDraft = request.IsDraft, IsFinalized = request.IsFinalized, CreatedByUserId = _actor.UserId?.ToString("D"), CreatedByDisplay = _actor.AuditLabel };
        });
    }

    public async Task<string?> GetDocumentExportContentAsync(long id)
    {
        var record = await GetDocumentExportByIdAsync(id);
        if (record is null) return null;
        if (!string.IsNullOrEmpty(record.ContentText)) return record.ContentText;
        return await _documents.ReadTextAsync(record.OutputPath);
    }

    public async Task<List<ReferenceDocument>> GetReferenceLibraryAsync()
    {
        var catalog = new (string Key, string Title, string Description, string FileName)[]
        {
            ("opening_statement_reyes", "Opening Statement — Reyes (Prior Case)", "Real opening statement from a prior ARDOT condemnation trial. Reference only — copy from, don't auto-generate.", "OpeningStatement_Reyes.txt"),
            ("direct_exam_fanning", "Direct Examination — Maxwell Fanning", "Prior-case direct examination outline for an appraisal witness.", "DirectExamination_MaxwellFanning.txt"),
            ("direct_exam_bartlett", "Direct Examination Questions — Ches Bartlett", "Prior-case direct examination question outline.", "DirectExamination_ChesBartlett.txt"),
            ("jury_instructions", "Jury Instructions (AMI Reference Library)", "Arkansas Model Instruction reference language for condemnation trials.", "JuryInstructions.txt")
        };

        var results = new List<ReferenceDocument>();
        foreach (var entry in catalog)
        {
            var path = Path.Combine(_paths.Config.ReferenceFolder, entry.FileName);
            var text = File.Exists(path) ? await File.ReadAllTextAsync(path) : "(Reference file not found on disk.)";
            results.Add(new ReferenceDocument { Key = entry.Key, Title = entry.Title, Description = entry.Description, Text = text });
        }

        return results;
    }

    public async Task<RiskAnalysisResult> PreviewRiskAnalysisAsync(long caseId, RiskAnalysisInput input)
    {
        var workspace = await GetCaseWorkspaceAsync(caseId) ?? throw new InvalidOperationException("Case not found.");
        input.CaseId = caseId;
        return RiskAnalysisEngine.Compute(workspace.Case, input);
    }

    // Always returns a result - if the case has no saved ledger yet, computes a fresh
    // all-zero one (same as opening a blank copy of the real spreadsheet) without persisting it.
    public async Task<RiskAnalysisResult> GetRiskAnalysisAsync(long caseId)
    {
        var workspace = await GetCaseWorkspaceAsync(caseId) ?? throw new InvalidOperationException("Case not found.");
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, narrative, rows_json, analysis_date, interest_rate, contingency_fee_percent, created_at, updated_at
            FROM risk_analyses WHERE case_id=@caseId
            """;
        cmd.Parameters.AddWithValue("@caseId", caseId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var input = new RiskAnalysisInput
            {
                CaseId = caseId,
                Narrative = reader.IsDBNull(1) ? null : reader.GetString(1),
                AnalysisDate = reader.IsDBNull(3) ? null : reader.GetString(3),
                InterestRate = reader.IsDBNull(4) ? 0.06m : reader.GetDecimal(4),
                ContingencyFeePercent = reader.IsDBNull(5) ? 0.30m : reader.GetDecimal(5),
                Rows = JsonSerializer.Deserialize<List<RiskAnalysisRowInput>>(reader.GetString(2)) ?? []
            };
            var result = RiskAnalysisEngine.Compute(workspace.Case, input);
            result.Id = reader.GetInt64(0);
            result.CreatedAt = reader.GetString(6);
            result.UpdatedAt = reader.GetString(7);
            return result;
        }

        return RiskAnalysisEngine.Compute(workspace.Case, new RiskAnalysisInput { CaseId = caseId });
    }

    public async Task<List<RiskAnalysisHistoryRecord>> GetRiskAnalysisHistoryAsync(long caseId)
    {
        var list = new List<RiskAnalysisHistoryRecord>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, case_id, analysis_date, formula_version, narrative, rows_json, interest_rate, contingency_fee_percent, key_scenario_label, key_scenario_value, key_scenario_order, created_at FROM risk_analysis_history WHERE case_id=@caseId ORDER BY created_at DESC";
        cmd.Parameters.AddWithValue("@caseId", (object?)caseId ?? DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new RiskAnalysisHistoryRecord
            {
                Id = reader.GetInt64(0), CaseId = reader.GetInt64(1), AnalysisDate = reader.GetString(2),
                FormulaVersion = reader.GetString(3), Narrative = reader.IsDBNull(4) ? null : reader.GetString(4),
                Rows = JsonSerializer.Deserialize<List<RiskAnalysisRowInput>>(reader.GetString(5)) ?? [], InterestRate = reader.IsDBNull(6) ? 0.06m : reader.GetDecimal(6), ContingencyFeePercent = reader.IsDBNull(7) ? 0.30m : reader.GetDecimal(7), KeyScenarioLabel = reader.IsDBNull(8) ? null : reader.GetString(8), KeyScenarioValue = reader.IsDBNull(9) ? null : reader.GetDecimal(9), KeyScenarioOrder = reader.IsDBNull(10) ? null : reader.GetInt32(10), CreatedAt = reader.GetString(11)
            });
        }
        return list;
    }

    public async Task<RiskAnalysisResult> GetRiskAnalysisHistorySnapshotAsync(long caseId, long historyId)
    {
        var workspace = await GetCaseWorkspaceAsync(caseId) ?? throw new InvalidOperationException("Case not found.");
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, narrative, rows_json, analysis_date, interest_rate, contingency_fee_percent, created_at FROM risk_analysis_history WHERE id=@id AND case_id=@caseId";
        cmd.Parameters.AddWithValue("@id", historyId); cmd.Parameters.AddWithValue("@caseId", caseId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException("Risk analysis snapshot not found.");
        var result = RiskAnalysisEngine.Compute(workspace.Case, new RiskAnalysisInput { CaseId = caseId, Narrative = reader.IsDBNull(1) ? null : reader.GetString(1), AnalysisDate = reader.IsDBNull(3) ? null : reader.GetString(3), InterestRate = reader.IsDBNull(4) ? 0.06m : reader.GetDecimal(4), ContingencyFeePercent = reader.IsDBNull(5) ? 0.30m : reader.GetDecimal(5), Rows = JsonSerializer.Deserialize<List<RiskAnalysisRowInput>>(reader.GetString(2)) ?? [] });
        result.Id = reader.GetInt64(0); result.CreatedAt = reader.GetString(6); result.UpdatedAt = result.CreatedAt;
        return result;
    }

    public async Task DeleteRiskAnalysisHistoryAsync(long caseId, long historyId)
    {
        await WithWriteAsync(async (connection, tx) =>
        {
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM risk_analysis_history WHERE id=@id AND case_id=@caseId";
            cmd.Parameters.AddWithValue("@id", historyId); cmd.Parameters.AddWithValue("@caseId", caseId);
            var affected = await cmd.ExecuteNonQueryAsync();
            if (affected == 0) throw new InvalidOperationException("Risk analysis snapshot not found.");
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Deleted saved Risk Analysis {historyId} for case {caseId} at {DateTime.Now:G}");
            return 0;
        });
    }

    public async Task<int> DeleteSampleDataAsync()
    {
        return await WithWriteAsync(async (connection, tx) =>
        {
            var ids = new List<long>();
            var find = connection.CreateCommand();
            find.Transaction = tx;
            find.CommandText = "SELECT id FROM cases WHERE case_number LIKE 'SAMPLE-CASE-%' AND case_name LIKE 'Fictional Sample %'";
            await using (var reader = await find.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync()) ids.Add(reader.GetInt64(0));
            }

            foreach (var caseId in ids)
            {
                foreach (var sql in new[]
                         {
                             "DELETE FROM deadline_history WHERE deadline_id IN (SELECT id FROM deadlines WHERE case_id=@caseId)",
                             "DELETE FROM deadlines WHERE case_id=@caseId",
                             "DELETE FROM checklist_items WHERE case_id=@caseId",
                             "DELETE FROM discovery_tracking WHERE case_id=@caseId",
                             "DELETE FROM publication_dates WHERE case_id=@caseId",
                             "DELETE FROM case_issue_tags WHERE case_id=@caseId",
                             "DELETE FROM case_notes WHERE case_id=@caseId",
                             "DELETE FROM hearings WHERE case_id=@caseId",
                             "DELETE FROM document_exports WHERE case_id=@caseId",
                             "DELETE FROM valuation_positions WHERE case_id=@caseId",
                             "DELETE FROM comparable_sales WHERE case_id=@caseId",
                             "DELETE FROM witnesses WHERE case_id=@caseId",
                             "DELETE FROM exhibits WHERE case_id=@caseId",
                             "DELETE FROM trial_motions WHERE case_id=@caseId",
                             "DELETE FROM risk_analyses WHERE case_id=@caseId",
                             "DELETE FROM risk_analysis_offer_log WHERE case_id=@caseId",
                             "DELETE FROM cases WHERE id=@caseId"
                         })
                {
                    var cmd = connection.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = sql;
                    cmd.Parameters.AddWithValue("@caseId", caseId);
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            if (ids.Count > 0)
                await SetAppSettingAsync(connection, tx, "sample_data_removed_at", DateTime.UtcNow.ToString("O"));
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Removed {ids.Count} recognized sample case(s) at {DateTime.Now:G}");
            return ids.Count;
        });
    }

    public async Task ResetEntireDatabaseAsync(string scope, string confirmation)
    {
        if (!string.Equals(confirmation, "RESET CASE PLANNER", StringComparison.Ordinal))
            throw new InvalidOperationException("Type RESET CASE PLANNER exactly to confirm the reset.");
        if (scope is not ("database" or "database-and-generated-content"))
            throw new InvalidOperationException("Choose a valid reset scope.");
        if (!_paths.IsSafeWritableDatabase(out var message))
            throw new InvalidOperationException($"Writes are disabled for the current database. {message}");

        await _writeGate.WaitAsync();
        try
        {
            _paths.EnsureFolders();
            var backup = await BackupDatabaseAsync();
            if (!File.Exists(backup) || new FileInfo(backup).Length == 0)
                throw new InvalidOperationException("The required safety backup could not be verified, so the reset was canceled.");
            await LogAsync($"Full database reset requested; verified safety backup created at {backup}; scope={scope}");
            // SQLite keeps pooled handles alive on Windows, so replacing the file directly can
            // fail even though every repository query has completed. Clear the existing schema
            // in-place instead; this is transactional and keeps the server process usable.
            await using (var connection = new SqliteConnection(ConnectionString))
            {
                await connection.OpenAsync();
                var pragma = connection.CreateCommand();
                pragma.CommandText = "PRAGMA foreign_keys = OFF";
                await pragma.ExecuteNonQueryAsync();
                await using var tx = connection.BeginTransaction();
                var tables = new List<string>();
                var tableCmd = connection.CreateCommand();
                tableCmd.Transaction = tx;
                tableCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
                await using (var reader = await tableCmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
                }
                foreach (var table in tables)
                {
                    var clear = connection.CreateCommand();
                    clear.Transaction = tx;
                    clear.CommandText = $"DELETE FROM [{table.Replace("]", "]]", StringComparison.Ordinal)}]";
                    await clear.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();
            }
            if (scope == "database-and-generated-content" && Directory.Exists(_paths.Config.ExportsFolder))
            {
                foreach (var path in Directory.EnumerateFiles(_paths.Config.ExportsFolder))
                    try { File.Delete(path); } catch { }
            }
        }
        finally
        {
            _writeGate.Release();
        }

        await using (var seedConnection = new SqliteConnection(ConnectionString))
        {
            await seedConnection.OpenAsync();
            await EnsureIssueTagCatalogAsync(seedConnection);
            await SeedChecklistTemplatesAsync(seedConnection);
            await SeedDeadlineTemplatesAsync(seedConnection);
            await SeedAsync(seedConnection, true);
        }
        await InitializeAsync();
        await LogAsync($"Full database reset completed; scope={scope}; one fictional sample case reseeded.");
    }

    public async Task<string> GenerateRiskNarrativeAsync(long caseId, RiskNarrativeManualInputs manual)
    {
        var workspace = await GetCaseWorkspaceAsync(caseId) ?? throw new InvalidOperationException("Case not found.");
        var positions = await GetValuationPositionsAsync(caseId);
        var ashcPosition = positions.FirstOrDefault(p => p.Side == "ASHC");
        var landownerPosition = positions.FirstOrDefault(p => p.Side == "Landowner");
        var risk = await GetRiskAnalysisAsync(caseId);
        return DocumentCompositionRules.BuildRiskNarrative(workspace.Case, ashcPosition, landownerPosition, risk, manual);
    }

    public async Task<RiskAnalysisResult> SaveRiskAnalysisAsync(RiskAnalysisInput input)
    {
        return await WithWriteAsync(async (connection, tx) =>
        {
            var now = DateTime.UtcNow.ToString("O");
            var rowsJson = JsonSerializer.Serialize(input.Rows);
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO risk_analyses (case_id, narrative, rows_json, analysis_date, interest_rate, contingency_fee_percent, created_at, updated_at)
                VALUES (@case_id, @narrative, @rows_json, @analysis_date, @interest_rate, @contingency_fee_percent, @created_at, @updated_at)
                ON CONFLICT(case_id) DO UPDATE SET narrative=excluded.narrative, rows_json=excluded.rows_json, analysis_date=excluded.analysis_date, interest_rate=excluded.interest_rate, contingency_fee_percent=excluded.contingency_fee_percent, updated_at=excluded.updated_at;
                """;
            cmd.Parameters.AddWithValue("@case_id", input.CaseId);
            cmd.Parameters.AddWithValue("@narrative", DbValue(input.Narrative));
            cmd.Parameters.AddWithValue("@rows_json", rowsJson);
            cmd.Parameters.AddWithValue("@analysis_date", DbValue(input.AnalysisDate));
            cmd.Parameters.AddWithValue("@interest_rate", input.InterestRate <= 0 ? 0.06m : input.InterestRate);
            cmd.Parameters.AddWithValue("@contingency_fee_percent", input.ContingencyFeePercent < 0 ? 0.30m : input.ContingencyFeePercent);
            cmd.Parameters.AddWithValue("@created_at", now);
            cmd.Parameters.AddWithValue("@updated_at", now);
            await cmd.ExecuteNonQueryAsync();
            var keyScenario = input.Rows.Select((row, index) => new { row, index }).Where(x => x.row.JustCompensation is not null && x.row.JustCompensation > 0).LastOrDefault();
            var historyCmd = connection.CreateCommand();
            historyCmd.Transaction = tx;
            historyCmd.CommandText = "INSERT INTO risk_analysis_history (case_id, analysis_date, formula_version, narrative, rows_json, interest_rate, contingency_fee_percent, key_scenario_label, key_scenario_value, key_scenario_order, created_at) VALUES (@case_id, @analysis_date, 'risk-v1', @narrative, @rows_json, @interest_rate, @contingency_fee_percent, @key_scenario_label, @key_scenario_value, @key_scenario_order, @created_at)";
            historyCmd.Parameters.AddWithValue("@case_id", input.CaseId);
            historyCmd.Parameters.AddWithValue("@analysis_date", now[..10]);
            historyCmd.Parameters.AddWithValue("@narrative", DbValue(input.Narrative));
            historyCmd.Parameters.AddWithValue("@rows_json", rowsJson);
            historyCmd.Parameters.AddWithValue("@interest_rate", input.InterestRate <= 0 ? 0.06m : input.InterestRate);
            historyCmd.Parameters.AddWithValue("@contingency_fee_percent", input.ContingencyFeePercent < 0 ? 0.30m : input.ContingencyFeePercent);
            historyCmd.Parameters.AddWithValue("@key_scenario_label", DbValue(keyScenario?.row.Label));
            historyCmd.Parameters.AddWithValue("@key_scenario_value", keyScenario?.row.JustCompensation is { } keyValue ? keyValue : DBNull.Value);
            historyCmd.Parameters.AddWithValue("@key_scenario_order", keyScenario?.index is { } keyIndex ? keyIndex : DBNull.Value);
            historyCmd.Parameters.AddWithValue("@created_at", now);
            await historyCmd.ExecuteNonQueryAsync();
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Saved Risk Analysis for case {input.CaseId} at {DateTime.Now:G}");

            var idCmd = connection.CreateCommand();
            idCmd.Transaction = tx;
            idCmd.CommandText = "SELECT id FROM risk_analyses WHERE case_id=@case_id";
            idCmd.Parameters.AddWithValue("@case_id", input.CaseId);
            var id = Convert.ToInt64(await idCmd.ExecuteScalarAsync());

            var workspace = await GetCaseWorkspaceAsync(input.CaseId) ?? throw new InvalidOperationException("Case not found.");
            var result = RiskAnalysisEngine.Compute(workspace.Case, input);
            result.Id = id;
            result.CreatedAt = now;
            result.UpdatedAt = now;
            return result;
        });
    }

    public async Task DeleteRiskAnalysisAsync(long caseId)
    {
        await WithWriteAsync(async (connection, tx) =>
        {
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM risk_analyses WHERE case_id=@case_id";
            cmd.Parameters.AddWithValue("@case_id", caseId);
            await cmd.ExecuteNonQueryAsync();
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Reset Risk Analysis for case {caseId} at {DateTime.Now:G}");
            return 0;
        });
    }

    public async Task<List<RiskAnalysisOfferLogEntry>> GetOfferLogAsync(long caseId)
    {
        var list = new List<RiskAnalysisOfferLogEntry>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, case_id, offer_date, party, amount, updated_at
            FROM risk_analysis_offer_log WHERE case_id=@caseId
            ORDER BY COALESCE(offer_date, '9999-12-31')
            """;
        cmd.Parameters.AddWithValue("@caseId", caseId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new RiskAnalysisOfferLogEntry
            {
                Id = reader.GetInt64(0),
                CaseId = reader.GetInt64(1),
                OfferDate = NormalizeDate(reader.IsDBNull(2) ? null : reader.GetString(2)),
                Party = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Amount = reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                UpdatedAt = reader.IsDBNull(5) ? null : reader.GetString(5)
            });
        }

        return list;
    }

    public async Task<RiskAnalysisOfferLogEntry> SaveOfferLogEntryAsync(RiskAnalysisOfferLogEntry model)
    {
        return await WithWriteAsync(async (connection, tx) =>
        {
            var now = DateTime.UtcNow.ToString("O");
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            if (model.Id == 0)
            {
                cmd.CommandText = """
                    INSERT INTO risk_analysis_offer_log (case_id, offer_date, party, amount, created_at, updated_at)
                    VALUES (@case_id, @offer_date, @party, @amount, @created_at, @updated_at);
                    SELECT last_insert_rowid();
                    """;
                cmd.Parameters.AddWithValue("@created_at", now);
            }
            else
            {
                cmd.CommandText = """
                    UPDATE risk_analysis_offer_log SET offer_date=@offer_date, party=@party, amount=@amount, updated_at=@updated_at
                    WHERE id=@id;
                    SELECT @id;
                    """;
                cmd.Parameters.AddWithValue("@id", model.Id);
            }

            cmd.Parameters.AddWithValue("@case_id", model.CaseId);
            cmd.Parameters.AddWithValue("@offer_date", DbValue(model.OfferDate));
            cmd.Parameters.AddWithValue("@party", DbValue(model.Party));
            cmd.Parameters.AddWithValue("@amount", model.Amount.HasValue ? model.Amount.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@updated_at", now);
            model.Id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            model.UpdatedAt = now;
            return model;
        });
    }

    public async Task DeleteOfferLogEntryAsync(long id)
    {
        await WithWriteAsync(async (connection, tx) =>
        {
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM risk_analysis_offer_log WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
            return 0;
        });
    }

    public async Task<List<ServiceLogEntry>> GetServiceLogEntriesAsync(long caseId)
    {
        var list = new List<ServiceLogEntry>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, case_id, party_name, status, method, event_date, notes, created_at, updated_at
            FROM service_log_entries WHERE case_id=@caseId
            ORDER BY party_name, COALESCE(event_date, '9999-12-31')
            """;
        cmd.Parameters.AddWithValue("@caseId", caseId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new ServiceLogEntry
            {
                Id = reader.GetInt64(0),
                CaseId = reader.GetInt64(1),
                PartyName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Status = reader.IsDBNull(3) ? "Not Served" : reader.GetString(3),
                Method = reader.IsDBNull(4) ? null : reader.GetString(4),
                EventDate = NormalizeDate(reader.IsDBNull(5) ? null : reader.GetString(5)),
                Notes = reader.IsDBNull(6) ? null : reader.GetString(6),
                CreatedAt = reader.IsDBNull(7) ? null : reader.GetString(7),
                UpdatedAt = reader.IsDBNull(8) ? null : reader.GetString(8),
            });
        }

        return list;
    }

    public async Task<ServiceLogEntry> SaveServiceLogEntryAsync(ServiceLogEntry model)
    {
        return await WithWriteAsync(async (connection, tx) =>
        {
            var now = DateTime.UtcNow.ToString("O");
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            if (model.Id == 0)
            {
                cmd.CommandText = """
                    INSERT INTO service_log_entries (case_id, party_name, status, method, event_date, notes, created_at, updated_at)
                    VALUES (@case_id, @party_name, @status, @method, @event_date, @notes, @created_at, @updated_at);
                    SELECT last_insert_rowid();
                    """;
                cmd.Parameters.AddWithValue("@created_at", now);
            }
            else
            {
                cmd.CommandText = """
                    UPDATE service_log_entries SET party_name=@party_name, status=@status, method=@method, event_date=@event_date, notes=@notes, updated_at=@updated_at
                    WHERE id=@id;
                    SELECT @id;
                    """;
                cmd.Parameters.AddWithValue("@id", model.Id);
            }

            cmd.Parameters.AddWithValue("@case_id", model.CaseId);
            cmd.Parameters.AddWithValue("@party_name", DbValue(model.PartyName));
            cmd.Parameters.AddWithValue("@status", DbValue(model.Status));
            cmd.Parameters.AddWithValue("@method", DbValue(model.Method));
            cmd.Parameters.AddWithValue("@event_date", DbValue(model.EventDate));
            cmd.Parameters.AddWithValue("@notes", DbValue(model.Notes));
            cmd.Parameters.AddWithValue("@updated_at", now);
            model.Id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            model.UpdatedAt = now;
            return model;
        });
    }

    public async Task DeleteServiceLogEntryAsync(long id)
    {
        await WithWriteAsync(async (connection, tx) =>
        {
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM service_log_entries WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
            return 0;
        });
    }

    public async Task<ImportResult> ImportCasesCsvAsync(Stream stream)
    {
        return await WithWriteAsync(async (connection, tx) =>
        {
            var result = new ImportResult();
            using var parser = new TextFieldParser(stream)
            {
                TextFieldType = FieldType.Delimited,
                HasFieldsEnclosedInQuotes = true
            };
            parser.SetDelimiters(",");
            if (parser.EndOfData)
            {
                return result;
            }

            var headers = parser.ReadFields() ?? [];
            var map = headers
                .Select((header, index) => new { Header = header.Trim(), Index = index })
                .ToDictionary(x => x.Header, x => x.Index, StringComparer.OrdinalIgnoreCase);

            while (!parser.EndOfData)
            {
                var row = parser.ReadFields();
                if (row is null)
                {
                    continue;
                }

                result.RowsRead++;
                try
                {
                    var caseNumber = GetField(row, map, "Case Number");
                    var jobNumber = GetField(row, map, "Job Number");
                    var tract = GetField(row, map, "Tract");
                    if (string.IsNullOrWhiteSpace(caseNumber) && string.IsNullOrWhiteSpace(jobNumber) && string.IsNullOrWhiteSpace(tract))
                    {
                        result.Skipped++;
                        result.Errors.Add($"Row {result.RowsRead}: missing identifier.");
                        continue;
                    }

                    var existing = await FindMatchingCaseAsync(connection, tx, caseNumber, jobNumber, tract);
                    var existingId = existing?.Id;
                    var sheetStatus = GetField(row, map, "Status");
                    // Newly imported cases land in Triage (no deadline/alert generation) until the
                    // triage wizard confirms track/stage/historical dates and activates them. A
                    // re-import of a case still in Triage keeps it in Triage; other existing cases
                    // take the sheet's status. Closed imports stay Closed (already alert-silent).
                    var importStatus = existing is null
                        ? (sheetStatus is "Closed" or "Complete" ? sheetStatus : "Triage")
                        : (existing.Value.Status == "Triage" ? "Triage" : sheetStatus);
                    var model = new CaseRecord
                    {
                        Id = existingId ?? 0,
                        CaseNumber = caseNumber,
                        CaseName = GetField(row, map, "Case Name"),
                        JobNumber = jobNumber,
                        Tract = tract,
                        County = GetField(row, map, "County"),
                        Status = importStatus,
                        FilingDate = NormalizeDate(GetField(row, map, "Filing Date")),
                        DateOfTaking = NormalizeDate(GetField(row, map, "Date of Taking")),
                        TrialDate = NormalizeDate(GetField(row, map, "Trial Date")),
                        TrialEndDate = NormalizeDate(GetField(row, map, "Trial End Date")),
                        PropertyDescription = BlankToNull(GetField(row, map, "Property Description")),
                        NextAction = BlankToNull(GetField(row, map, "Next Action")),
                        NextActionDue = NormalizeDate(GetField(row, map, "Next Action Due")),
                        DepositAmount = ParseMoney(GetField(row, map, "Deposit Amount")),
                        Owner = BlankToNull(GetField(row, map, "Owner")),
                        Landowner = BlankToNull(GetField(row, map, "Landowner")),
                        PublicationServiceNotes = BlankToNull(GetField(row, map, "Notes")),
                        ServiceRequired = ParseBool(GetField(row, map, "Service Required"), true),
                        ServicePerfected = ParseBool(GetField(row, map, "Service Perfected")),
                        ServicePerfectedDate = NormalizeDate(GetField(row, map, "Service Perfected Date")),
                        ServiceDeadlineBasisDate = NormalizeDate(GetField(row, map, "Service Deadline Basis Date")),
                        ServiceDeadline120 = NormalizeDate(GetField(row, map, "Service Deadline 120")),
                        ServiceMethod = BlankToNull(GetField(row, map, "Service Method")),
                        ServiceStatus = BlankToNull(GetField(row, map, "Service Status")),
                        ServiceNotes = BlankToNull(GetField(row, map, "Service Notes"))
                    };

                    await SaveCaseInternalAsync(connection, tx, model);
                    if (existingId is null)
                    {
                        result.Created++;
                    }
                    else
                    {
                        result.Updated++;
                    }
                }
                catch (Exception ex)
                {
                    result.Skipped++;
                    result.Errors.Add($"Row {result.RowsRead}: {ex.Message}");
                }
            }

            await SetAppSettingAsync(connection, tx, "last_import_result", $"rows={result.RowsRead}; created={result.Created}; updated={result.Updated}; skipped={result.Skipped}; errors={result.Errors.Count}");
            return result;
        });
    }

    public async Task<ImportResult> ImportCasesXlsxAsync(Stream stream)
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
        return await WithWriteAsync(async (connection, tx) =>
        {
            var result = new ImportResult();
            var jobNumbersInDiscoverySheet = ReadDiscoverySheetJobNumbers(workbook);

            foreach (var sheetName in new[] { "Open", "Closed" })
            {
                if (!workbook.TryGetWorksheet(sheetName, out var sheet))
                {
                    result.Info.Add($"Sheet '{sheetName}' not found; skipped.");
                    continue;
                }

                var isClosedSheet = sheetName == "Closed";
                var headerRow = sheet.FirstRowUsed();
                if (headerRow is null)
                {
                    continue;
                }

                var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var cell in headerRow.CellsUsed())
                {
                    map[cell.GetString().Trim()] = cell.Address.ColumnNumber;
                }

                foreach (var row in sheet.RowsUsed().Skip(1))
                {
                    var caseNumber = CellText(row, map, "CASE NO.");
                    var jobNumber = CellText(row, map, "JOB");
                    var tract = CellText(row, map, "TRACT NO.");
                    var caseName = CellText(row, map, "CASE NAME");
                    if (string.IsNullOrWhiteSpace(caseNumber) && string.IsNullOrWhiteSpace(jobNumber) && string.IsNullOrWhiteSpace(caseName))
                    {
                        continue;
                    }

                    result.RowsRead++;
                    try
                    {
                        var existing = await FindMatchingCaseAsync(connection, tx, caseNumber, jobNumber, tract);
                        var existingId = existing?.Id;
                        var proofOfPublication = CellText(row, map, "PROOF OF PUBLICATION");
                        var inDiscovery = !isClosedSheet && jobNumber != "" && jobNumbersInDiscoverySheet.Contains(jobNumber);
                        // New open-sheet cases enter Triage until the wizard activates them; a
                        // re-import keeps an in-progress Triage case in Triage but never knocks an
                        // already-active case back into triage.
                        var openSheetStatus = existing is null || existing.Value.Status == "Triage" ? "Triage" : "Active";
                        var model = new CaseRecord
                        {
                            Id = existingId ?? 0,
                            CaseNumber = caseNumber,
                            CaseName = string.IsNullOrWhiteSpace(caseName) ? caseNumber : caseName,
                            JobNumber = jobNumber,
                            Tract = tract,
                            County = CellText(row, map, "COUNTY"),
                            Status = isClosedSheet ? "Closed" : openSheetStatus,
                            Stage = inDiscovery ? "Discovery & Evaluation" : "",
                            FilingDate = CellDate(row, map, "DATE FILED"),
                            DateOfTaking = CellDate(row, map, "DATE OF TAKING"),
                            TrialDate = CellDate(row, map, "TRIAL DATE"),
                            TrialEndDate = CellDate(row, map, "TRIAL END DATE"),
                            PropertyDescription = BlankToNull(CellText(row, map, "PROPERTY DESCRIPTION")),
                            DateOpened = CellDate(row, map, "DATE OPENED"),
                            DepositAmount = CellMoney(row, map, "DEPOSIT"),
                            PublicationServiceNotes = BlankToNull(CellText(row, map, "NOTES")),
                            ServiceNotes = string.IsNullOrWhiteSpace(proofOfPublication) ? null : $"Proof of publication: {proofOfPublication}",
                            AssignedAttorney = BlankToNull(CellText(row, map, "ATTY")),
                            OpposingCounsel = BlankToNull(CellText(row, map, "ATTORNEY")),
                            Appraiser = BlankToNull(CellText(row, map, "APPR")),
                            TaxesOwed = BlankToNull(CellText(row, map, "TAXES OWED?")),
                            FundsWithdrawn = BlankToNull(CellText(row, map, "FUNDS W/D?")),
                            DiscoveryCompleted = BlankToNull(CellText(row, map, "DISCOVERY COMPLETED?")),
                            UpdatedAppraisal = BlankToNull(CellText(row, map, "UPDATED APPRAISAL?")),
                            ClosedDate = isClosedSheet ? CellDate(row, map, "CLOSED DATE") : null,
                            ServiceRequired = true
                        };

                        await SaveCaseInternalAsync(connection, tx, model);
                        if (existingId is null)
                        {
                            result.Created++;
                        }
                        else
                        {
                            result.Updated++;
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Skipped++;
                        result.Errors.Add($"{sheetName} row {row.RowNumber()}: {ex.Message}");
                    }
                }
            }

            var discoveryImported = await ImportDiscoverySheetAsync(workbook, connection, tx, result);
            if (discoveryImported > 0)
            {
                result.Info.Add($"Imported {discoveryImported} discovery tracking row(s) from the Discovery sheet.");
            }

            await SetAppSettingAsync(connection, tx, "last_import_result", $"Excel import: rows={result.RowsRead}; created={result.Created}; updated={result.Updated}; skipped={result.Skipped}; errors={result.Errors.Count}");
            return result;
        });
    }

    private static HashSet<string> ReadDiscoverySheetJobNumbers(ClosedXML.Excel.XLWorkbook workbook)
    {
        var jobs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!workbook.TryGetWorksheet("Discovery", out var sheet))
        {
            return jobs;
        }

        var headerRow = sheet.FirstRowUsed();
        if (headerRow is null)
        {
            return jobs;
        }

        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
        {
            map[cell.GetString().Trim()] = cell.Address.ColumnNumber;
        }

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var job = CellText(row, map, "Job No.");
            if (!string.IsNullOrWhiteSpace(job))
            {
                jobs.Add(job);
            }
        }

        return jobs;
    }

    private async Task<int> ImportDiscoverySheetAsync(ClosedXML.Excel.XLWorkbook workbook, SqliteConnection connection, SqliteTransaction tx, ImportResult result)
    {
        if (!workbook.TryGetWorksheet("Discovery", out var sheet))
        {
            return 0;
        }

        var headerRow = sheet.FirstRowUsed();
        if (headerRow is null)
        {
            return 0;
        }

        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
        {
            map[cell.GetString().Trim()] = cell.Address.ColumnNumber;
        }

        var imported = 0;
        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var job = CellText(row, map, "Job No.");
            var tract = CellText(row, map, "Tract");
            var lastName = CellText(row, map, "Last Name");
            if (string.IsNullOrWhiteSpace(job) && string.IsNullOrWhiteSpace(lastName))
            {
                continue;
            }

            var caseId = (await FindMatchingCaseAsync(connection, tx, "", job, tract))?.Id;
            if (caseId is null && !string.IsNullOrWhiteSpace(job))
            {
                // Fall back to job-number-only match (tract formats often differ between sheets).
                var jobCmd = connection.CreateCommand();
                jobCmd.Transaction = tx;
                jobCmd.CommandText = "SELECT id FROM cases WHERE job_number = @job LIMIT 1";
                jobCmd.Parameters.AddWithValue("@job", job);
                var scalar = await jobCmd.ExecuteScalarAsync();
                caseId = scalar is null ? null : Convert.ToInt64(scalar);
            }

            if (caseId is null)
            {
                result.Errors.Add($"Discovery row {row.RowNumber()}: no matching case for job '{job}' / '{lastName}'; skipped.");
                continue;
            }

            // Skip if this case already has an imported discovery row (idempotent re-import).
            var dupCmd = connection.CreateCommand();
            dupCmd.Transaction = tx;
            dupCmd.CommandText = "SELECT COUNT(*) FROM discovery_tracking WHERE case_id = @caseId AND notes LIKE '%[Imported from Excel]%'";
            dupCmd.Parameters.AddWithValue("@caseId", caseId);
            if (Convert.ToInt32(await dupCmd.ExecuteScalarAsync()) > 0)
            {
                continue;
            }

            var responsesText = CellText(row, map, "Responses Rec'd");
            var responseDate = NormalizeDate(responsesText);
            var notesParts = new List<string>();
            var sheetNotes = CellText(row, map, "Notes");
            if (!string.IsNullOrWhiteSpace(sheetNotes))
            {
                notesParts.Add(sheetNotes);
            }
            if (responseDate is null && !string.IsNullOrWhiteSpace(responsesText))
            {
                notesParts.Add($"Responses: {responsesText}");
            }
            notesParts.Add("[Imported from Excel]");

            var item = new DiscoveryItemRecord
            {
                CaseId = caseId.Value,
                Direction = "Served by Us",
                DiscoveryType = "Interrogatories / RFP",
                ServedDate = CellDate(row, map, "Date Disc. Sent."),
                DueDate = CellDate(row, map, "Due Date"),
                ResponseDate = responseDate,
                FollowUpDate = CellDate(row, map, "GF Deadline"),
                GoodFaithSentDate = CellDate(row, map, "Good Faith Sent"),
                MotionToCompelDate = CellDate(row, map, "MTC/MTD Filed"),
                Status = responseDate is not null ? "Responses Received" : "Waiting for Responses",
                Notes = string.Join(" | ", notesParts)
            };

            var now = DateTime.UtcNow.ToString("O");
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO discovery_tracking (
                    case_id, direction, discovery_type, served_date, due_date, response_date,
                    follow_up_date, status, assigned_to, notes, good_faith_sent_date, motion_to_compel_date, created_at, updated_at
                ) VALUES (
                    @case_id, @direction, @discovery_type, @served_date, @due_date, @response_date,
                    @follow_up_date, @status, NULL, @notes, @good_faith_sent_date, @motion_to_compel_date, @now, @now
                )
                """;
            cmd.Parameters.AddWithValue("@case_id", item.CaseId);
            cmd.Parameters.AddWithValue("@direction", item.Direction);
            cmd.Parameters.AddWithValue("@discovery_type", item.DiscoveryType);
            cmd.Parameters.AddWithValue("@served_date", DbValue(item.ServedDate));
            cmd.Parameters.AddWithValue("@due_date", DbValue(item.DueDate));
            cmd.Parameters.AddWithValue("@response_date", DbValue(item.ResponseDate));
            cmd.Parameters.AddWithValue("@follow_up_date", DbValue(item.FollowUpDate));
            cmd.Parameters.AddWithValue("@status", item.Status);
            cmd.Parameters.AddWithValue("@notes", DbValue(item.Notes));
            cmd.Parameters.AddWithValue("@good_faith_sent_date", DbValue(item.GoodFaithSentDate));
            cmd.Parameters.AddWithValue("@motion_to_compel_date", DbValue(item.MotionToCompelDate));
            cmd.Parameters.AddWithValue("@now", now);
            await cmd.ExecuteNonQueryAsync();
            imported++;
        }

        return imported;
    }

    private static string CellText(ClosedXML.Excel.IXLRow row, Dictionary<string, int> map, string header)
    {
        if (!map.TryGetValue(header, out var column))
        {
            return "";
        }

        var cell = row.Cell(column);
        if (cell.DataType == ClosedXML.Excel.XLDataType.DateTime)
        {
            return cell.GetDateTime().ToString("yyyy-MM-dd");
        }

        return cell.GetString().Trim();
    }

    private static string? CellDate(ClosedXML.Excel.IXLRow row, Dictionary<string, int> map, string header)
    {
        if (!map.TryGetValue(header, out var column))
        {
            return null;
        }

        var cell = row.Cell(column);
        if (cell.DataType == ClosedXML.Excel.XLDataType.DateTime)
        {
            var value = DateOnly.FromDateTime(cell.GetDateTime());
            return value == new DateOnly(1900, 1, 1) ? null : value.ToString("yyyy-MM-dd");
        }

        return NormalizeDate(cell.GetString().Trim());
    }

    private static decimal? CellMoney(ClosedXML.Excel.IXLRow row, Dictionary<string, int> map, string header)
    {
        if (!map.TryGetValue(header, out var column))
        {
            return null;
        }

        var cell = row.Cell(column);
        if (cell.DataType == ClosedXML.Excel.XLDataType.Number)
        {
            return (decimal)cell.GetDouble();
        }

        return ParseMoney(cell.GetString());
    }

    public async Task<DiagnosticsSnapshot> GetDiagnosticsAsync()
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var safe = _paths.IsSafeWritableDatabase(out var message);
        return new DiagnosticsSnapshot
        {
            AppName = _paths.Config.AppName,
            Version = _paths.Config.Version,
            DatabaseProvider = "SQLite (active runtime); SQL Server migration target available",
            DatabaseArchitectureNote = "The current repository still reads and writes SQLite. The shared data layer, target probe, migration utility, and SQL Server multi-user schema are migration foundations; SQL Server runtime cutover remains intentionally disabled until repository reconciliation, identity, and authorization are complete.",
            DatabasePath = _paths.Config.DatabasePath,
            DatabaseWritable = PathService.CanWriteFile(_paths.Config.DatabasePath),
            BackupsWritable = PathService.CanWrite(_paths.Config.BackupsFolder),
            ExportsWritable = PathService.CanWrite(_paths.Config.ExportsFolder),
            LogsWritable = PathService.CanWrite(_paths.Config.LogsFolder),
            WriteSafetyOk = safe,
            WriteSafetyMessage = message,
            CaseCount = await ScalarCountAsync(connection, "cases"),
            DeadlineCount = await ScalarCountAsync(connection, "deadlines"),
            ChecklistCount = await ScalarCountAsync(connection, "checklist_items"),
            DiscoveryCount = await ScalarCountAsync(connection, "discovery_tracking"),
            DocumentExportCount = await ScalarCountAsync(connection, "document_exports"),
            SampleDataExists = await ScalarCountAsync(connection, "cases WHERE case_number LIKE 'SAMPLE-CASE-%' AND case_name LIKE 'Fictional Sample %'") > 0,
            LastImportResult = await GetAppSettingAsync(connection, "last_import_result"),
            LastDocumentGenerationResult = await GetAppSettingAsync(connection, "last_document_generation_result"),
            StageMigrationReview = await GetAppSettingAsync(connection, "stage_migration_v2_review"),
            LatestLogPath = Directory.Exists(_paths.Config.LogsFolder)
                ? Directory.GetFiles(_paths.Config.LogsFolder, "*.log").OrderByDescending(File.GetLastWriteTime).FirstOrDefault()
                : null,
            Folders = new Dictionary<string, string>
            {
                ["data"] = _paths.Config.DataFolder,
                ["backups"] = _paths.Config.BackupsFolder,
                ["exports"] = _paths.Config.ExportsFolder,
                ["templates"] = _paths.Config.TemplatesFolder,
                ["logs"] = _paths.Config.LogsFolder
            }
        };
    }

    private static List<ServiceQueueItem> BuildServiceSummaries(IEnumerable<CaseRecord> cases, IEnumerable<PublicationRecord> publicationEntries)
        => ServiceStatusEngine.BuildQueue(cases, publicationEntries);

    private static ServiceStatusSummary BuildServiceStatus(CaseRecord caseRecord, PublicationRecord? publication)
        => ServiceStatusEngine.Build(caseRecord, publication);

    public async Task LogAsync(string message)
    {
        Directory.CreateDirectory(_paths.Config.LogsFolder);
        var path = Path.Combine(_paths.Config.LogsFolder, $"web_{DateTime.Now:yyyyMMdd}.log");
        await File.AppendAllTextAsync(path, $"[{DateTime.Now:O}] {message}{Environment.NewLine}");
    }

    private async Task EnsureSchemaUpgradesAsync(SqliteConnection connection)
    {
        // Build-plan step 5: issue tags predate the "create/rename/retire" admin screen and never
        // had a soft-delete column - retiring a tag must not remove it outright (case history and
        // document-template sections may still reference it by name), and EnsureIssueTagCatalogAsync's
        // "insert if no row exists with this name" seeding already leaves a merely-flagged row alone.
        await AddColumnIfMissingAsync(connection, "issue_tags", "is_deleted", "INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfMissingAsync(connection, "cases", "service_required", "INTEGER DEFAULT 1");
        await AddColumnIfMissingAsync(connection, "cases", "service_perfected", "INTEGER DEFAULT 0");
        await AddColumnIfMissingAsync(connection, "cases", "service_perfected_date", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "service_deadline_120", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "service_deadline_basis_date", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "service_method", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "service_notes", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "service_status", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "stage", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "case_type", "TEXT DEFAULT 'Standard'");
        await AddColumnIfMissingAsync(connection, "cases", "track", "TEXT DEFAULT 'Contested'");
        await AddColumnIfMissingAsync(connection, "checklist_templates", "track", "TEXT DEFAULT 'Any'");
        // Item 2 (multi-user rollout Phase 2): opaque passthrough on SQLite (no app_users table
        // here to validate against) - only meaningfully populated/selectable once Entra is enabled.
        await AddColumnIfMissingAsync(connection, "checklist_items", "assigned_user_id", "TEXT");
        // Multi-user rollout Phase 3 (shared witness registry): links a per-case witness row to
        // the new global witness_persons identity. Nullable so pre-existing rows (and the
        // one-time migration that backfills them) can be told apart from never-linked ones.
        await AddColumnIfMissingAsync(connection, "witnesses", "person_id", "INTEGER");
        await AddColumnIfMissingAsync(connection, "cases", "assigned_attorney", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "opposing_counsel", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "appraiser", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "taxes_owed", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "funds_withdrawn", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "funds_withdrawn_date", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "discovery_completed", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "updated_appraisal", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "closed_date", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "date_opened", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "project_name", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "tax_owed_amount", "REAL");
        await AddColumnIfMissingAsync(connection, "cases", "whole_property_acres", "REAL");
        await AddColumnIfMissingAsync(connection, "cases", "acquisition_acres", "REAL");
        await AddColumnIfMissingAsync(connection, "cases", "landowner_appraiser_name", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "additional_deposit_amount", "REAL");
        await AddColumnIfMissingAsync(connection, "cases", "additional_deposit_date", "TEXT");
        await AddColumnIfMissingAsync(connection, "deadlines", "severity", "TEXT DEFAULT 'normal'");
        await AddColumnIfMissingAsync(connection, "deadlines", "completed_at", "TEXT");
        await AddColumnIfMissingAsync(connection, "checklist_items", "completed_at", "TEXT");
        foreach (var table in new[] { "deadlines", "checklist_items" })
        {
            await AddColumnIfMissingAsync(connection, table, "source_kind", "TEXT DEFAULT 'Manual'");
            await AddColumnIfMissingAsync(connection, table, "source_template_id", "TEXT");
            await AddColumnIfMissingAsync(connection, table, "source_template_version", "INTEGER");
            await AddColumnIfMissingAsync(connection, table, "source_stage", "TEXT");
            await AddColumnIfMissingAsync(connection, table, "generated_at", "TEXT");
            await AddColumnIfMissingAsync(connection, table, "generated_by", "TEXT");
        }
        await AddColumnIfMissingAsync(connection, "discovery_tracking", "request_title", "TEXT");
        await AddColumnIfMissingAsync(connection, "discovery_tracking", "escalation_note", "TEXT");
        await AddColumnIfMissingAsync(connection, "risk_analysis_offer_log", "updated_at", "TEXT");
        await AddColumnIfMissingAsync(connection, "hearings", "event_type", "TEXT NOT NULL DEFAULT 'Hearing'");
        await AddColumnIfMissingAsync(connection, "hearings", "status", "TEXT NOT NULL DEFAULT 'Scheduled'");
        await AddColumnIfMissingAsync(connection, "risk_analyses", "analysis_date", "TEXT");
        await AddColumnIfMissingAsync(connection, "risk_analyses", "interest_rate", "REAL NOT NULL DEFAULT 0.06");
        await AddColumnIfMissingAsync(connection, "risk_analyses", "contingency_fee_percent", "REAL NOT NULL DEFAULT 0.30");
        await AddColumnIfMissingAsync(connection, "risk_analysis_history", "interest_rate", "REAL NOT NULL DEFAULT 0.06");
        await AddColumnIfMissingAsync(connection, "risk_analysis_history", "contingency_fee_percent", "REAL NOT NULL DEFAULT 0.30");
        await AddColumnIfMissingAsync(connection, "risk_analysis_history", "key_scenario_label", "TEXT");
        await AddColumnIfMissingAsync(connection, "risk_analysis_history", "key_scenario_value", "REAL");
        await AddColumnIfMissingAsync(connection, "risk_analysis_history", "key_scenario_order", "INTEGER");
        await AddColumnIfMissingAsync(connection, "document_exports", "content_text", "TEXT");
        await AddColumnIfMissingAsync(connection, "document_exports", "base_template_version", "TEXT");
        await AddColumnIfMissingAsync(connection, "document_exports", "issue_tag_versions", "TEXT");
        await AddColumnIfMissingAsync(connection, "document_exports", "is_draft", "INTEGER DEFAULT 0");
        await AddColumnIfMissingAsync(connection, "document_exports", "is_finalized", "INTEGER DEFAULT 1");
        await AddColumnIfMissingAsync(connection, "document_exports", "merge_field_values", "TEXT");
        await AddColumnIfMissingAsync(connection,"document_exports","created_by_user_id","TEXT");
        await AddColumnIfMissingAsync(connection,"document_exports","created_by_display","TEXT");
        await AddColumnIfMissingAsync(connection,"document_exports","qa_reviewed_by_user_id","TEXT");
        await AddColumnIfMissingAsync(connection,"document_exports","qa_reviewed_by_display","TEXT");
        await AddColumnIfMissingAsync(connection,"activity_log","actor_user_id","TEXT");
        await AddColumnIfMissingAsync(connection,"activity_log","actor_display","TEXT");
        await AddColumnIfMissingAsync(connection,"activity_log_history","edited_by_user_id","TEXT");
        await AddColumnIfMissingAsync(connection,"activity_log_history","edited_by_display","TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "matter_type", "TEXT DEFAULT 'FiledCase'");
        await AddColumnIfMissingAsync(connection, "cases", "priority", "TEXT DEFAULT 'Normal'");
        await AddColumnIfMissingAsync(connection, "cases", "current_holder", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "pipeline_stage", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "date_sent_to_current_holder", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "next_review_date", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "deferred_until", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "deferred_reason", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "deferred_at", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "deferred_by", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "last_meaningful_activity_date", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "momentum_status", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "waiting_reason", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "waiting_on", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "waiting_started_date", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "expected_response", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "waiting_follow_up_date", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "waiting_escalation_action", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "trial_track", "INTEGER DEFAULT 0");
        await AddColumnIfMissingAsync(connection, "cases", "short_posture_summary", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "current_issue", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "case_status", "TEXT DEFAULT 'Pipeline'");
        await AddColumnIfMissingAsync(connection, "cases", "status_mapping_review", "INTEGER DEFAULT 0");
        await AddColumnIfMissingAsync(connection, "cases", "trial_end_date", "TEXT");
        await AddColumnIfMissingAsync(connection, "cases", "property_description", "TEXT");
        await AddColumnIfMissingAsync(connection, "discovery_postures", "completion_changed_at", "TEXT");
        await AddColumnIfMissingAsync(connection, "discovery_postures", "completion_changed_by", "TEXT");
        await MigrateLegacyStageNamesAsync(connection);
        await MigrateStageTrackUnificationV1Async(connection);
        await MigrateRiskAnalysesToSingleRecordAsync(connection);
        await MigrateBackfillMeaningfulActivityV1Async(connection);
        await MigrateRiskAnalysisRowsToListV1Async(connection);
        await MigrateCanonicalPublicationV1Async(connection);
        await MigrateDedicatedDefermentsV1Async(connection);
        await MigrateConsolidatedCaseStatusV1Async(connection);
        await MigrateChecklistTemplateWorkflowStatusesV1Async(connection);
        await MigrateStructuredProvenanceV1Async(connection);
        await ExecuteAsync(connection, CasesDashboardIndexSql);
    }

    private async Task MigrateConsolidatedCaseStatusV1Async(SqliteConnection connection)
    {
        const string flag = "consolidated_case_status_v1_complete";
        if (await GetAppSettingAsync(connection, flag) == "true") return;
        await ExecuteAsync(connection, """
            UPDATE cases SET
              case_status = CASE
                WHEN COALESCE(status,'') IN ('Triage') THEN 'Triage'
                WHEN COALESCE(status,'') IN ('Closed','Complete') OR COALESCE(stage,'')='Resolved' THEN 'Resolved / Closed'
                WHEN COALESCE(status,'')='Pipeline' OR (COALESCE(case_number,'')='' AND COALESCE(pipeline_stage,'')<>'') THEN 'Pipeline'
                WHEN COALESCE(track,'')='Settlement' THEN 'Settlement Pending'
                WHEN COALESCE(stage,'')='Trial Track' THEN 'Trial Preparation'
                WHEN COALESCE(stage,'')='Service' THEN 'Filed / Service Pending'
                ELSE 'Active Litigation'
              END,
              status_mapping_review = CASE
                WHEN COALESCE(status,'')='' OR (COALESCE(stage,'')='' AND COALESCE(status,'') NOT IN ('Pipeline','Triage','Closed','Complete')) THEN 1 ELSE 0 END
            WHERE COALESCE(case_status,'')='';
            """);
        await using var tx = connection.BeginTransaction();
        await SetAppSettingAsync(connection, tx, flag, "true");
        await tx.CommitAsync();
    }

    private async Task MigrateChecklistTemplateWorkflowStatusesV1Async(SqliteConnection connection)
    {
        const string flag = "checklist_template_workflow_statuses_v1_complete";
        if (await GetAppSettingAsync(connection, flag) == "true") return;

        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Intake & Filing"] = "Pipeline",
            ["Service"] = "Filed / Service Pending",
            ["Discovery & Evaluation"] = "Active Litigation",
            ["Trial Track"] = "Trial Preparation",
            ["Resolved"] = "Resolved / Closed"
        };
        foreach (var (legacy, workflow) in mappings)
        {
            foreach (var tableColumn in new[] { (Table: "checklist_templates", Column: "stage"), (Table: "checklist_template_items", Column: "phase") })
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = $"UPDATE {tableColumn.Table} SET {tableColumn.Column}=@workflow WHERE {tableColumn.Column}=@legacy";
                cmd.Parameters.AddWithValue("@workflow", workflow);
                cmd.Parameters.AddWithValue("@legacy", legacy);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        await using var tx = connection.BeginTransaction();
        await SetAppSettingAsync(connection, tx, flag, "true");
        await tx.CommitAsync();
    }

    public async Task<List<DeadlineTemplateRecord>> GetDeadlineTemplatesAsync()
    {
        var list = new List<DeadlineTemplateRecord>();
        await using var connection = new SqliteConnection(ConnectionString); await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id,name,trigger_field,offset_days,title,severity,track,active FROM deadline_templates ORDER BY name";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) list.Add(new DeadlineTemplateRecord { Id=reader.GetInt64(0), Name=reader.GetString(1), TriggerField=reader.GetString(2), OffsetDays=reader.GetInt32(3), Title=reader.GetString(4), Severity=reader.GetString(5), Track=reader.GetString(6), Active=reader.GetInt64(7)==1 });
        return list;
    }

    public async Task<DeadlineTemplateRecord> SaveDeadlineTemplateAsync(DeadlineTemplateRecord model) => await WithWriteAsync(async (connection, tx) =>
    {
        var now=DateTime.UtcNow.ToString("O"); var cmd=connection.CreateCommand(); cmd.Transaction=tx;
        if (model.Id==0) cmd.CommandText="""
            INSERT INTO deadline_templates(name,trigger_field,offset_days,title,severity,track,active,created_at,updated_at)
            VALUES(@name,@trigger,@offset,@title,@severity,@track,@active,@now,@now); SELECT last_insert_rowid();
            """;
        else { cmd.CommandText="""
            UPDATE deadline_templates SET name=@name,trigger_field=@trigger,offset_days=@offset,title=@title,severity=@severity,track=@track,active=@active,updated_at=@now WHERE id=@id; SELECT @id;
            """; cmd.Parameters.AddWithValue("@id",model.Id); }
        cmd.Parameters.AddWithValue("@name",model.Name); cmd.Parameters.AddWithValue("@trigger",model.TriggerField); cmd.Parameters.AddWithValue("@offset",model.OffsetDays); cmd.Parameters.AddWithValue("@title",model.Title); cmd.Parameters.AddWithValue("@severity",model.Severity); cmd.Parameters.AddWithValue("@track",model.Track); cmd.Parameters.AddWithValue("@active",model.Active?1:0); cmd.Parameters.AddWithValue("@now",now);
        model.Id=Convert.ToInt64(await cmd.ExecuteScalarAsync()); return model;
    });

    public async Task<List<WorkTemplateCandidate>> GetWorkTemplateCandidatesAsync(long caseId)
    {
        var ws = await GetCaseWorkspaceAsync(caseId) ?? throw new InvalidOperationException("Case not found.");
        var result = new List<WorkTemplateCandidate>();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var workflowStatus = string.IsNullOrWhiteSpace(ws.Case.CaseStatus)
            ? MapConsolidatedCaseStatus(ws.Case.Status, ws.Case.Stage, ws.Case.Track, ws.Case.CaseNumber, ws.Case.PipelineStage)
            : ws.Case.CaseStatus;
        foreach (var template in await GetChecklistTemplatesAsync())
        {
            if (!template.Active) continue;
            if (template.TriggerType == "Stage" && template.Stage != workflowStatus) continue;
            if (template.TriggerType == "IssueTag" && !ws.CaseIssueTags.Any(t => t.TagName.Equals(template.IssueTagName, StringComparison.OrdinalIgnoreCase))) continue;
            foreach (var item in template.Items)
            {
                var id = $"{template.Name}:{item.SortOrder}";
                var duplicate = ws.ChecklistItems.FirstOrDefault(x => x.SourceTemplateId == id || (x.Phase.Equals(item.Phase ?? workflowStatus, StringComparison.OrdinalIgnoreCase) && x.Task.Equals(item.Task, StringComparison.OrdinalIgnoreCase)));
                result.Add(new WorkTemplateCandidate { Kind="Task", TemplateId=id, TemplateVersion=1, Title=item.Task, Stage=item.Phase ?? workflowStatus, Track=template.Track,
                    DueDate=item.DueOffsetDays.HasValue?today.AddDays(item.DueOffsetDays.Value).ToString("yyyy-MM-dd"):null,
                    IsDuplicate=duplicate is not null, DuplicateReason=duplicate is null?null:$"Matches {duplicate.Status.ToLowerInvariant()} task: {duplicate.Task}" });
            }
        }
        foreach (var template in await GetDeadlineTemplatesAsync())
        {
            if (!template.Active) continue;
            var anchor = template.TriggerField switch { "filing_date"=>ParseDate(ws.Case.FilingDate), "trial_date"=>ParseDate(ws.Case.TrialDate), "service_perfected_date"=>ParseDate(ws.Case.ServicePerfectedDate), _=>null };
            var duplicate=ws.Deadlines.FirstOrDefault(x=>x.SourceTemplateId==template.Id.ToString() || x.Title.Equals(template.Title,StringComparison.OrdinalIgnoreCase));
            result.Add(new WorkTemplateCandidate { Kind="Deadline",TemplateId=template.Id.ToString(),TemplateVersion=3,Title=template.Title,Stage=workflowStatus,Track=template.Track,Severity=template.Severity,
                DueDate=anchor?.AddDays(template.OffsetDays).ToString("yyyy-MM-dd"),IsDuplicate=duplicate is not null,DuplicateReason=duplicate is null?null:$"Matches {duplicate.Status.ToLowerInvariant()} deadline: {duplicate.Title}" });
        }
        return result;
    }

    public async Task<int> AddWorkTemplateSelectionsAsync(long caseId, AddWorkTemplatesRequest request)
    {
        var candidates=(await GetWorkTemplateCandidatesAsync(caseId)).ToDictionary(x=>$"{x.Kind}:{x.TemplateId}");
        var added=0; var now=DateTime.UtcNow.ToString("O");
        foreach(var selection in request.Items)
        {
            if(!candidates.TryGetValue($"{selection.Kind}:{selection.TemplateId}",out var c) || (c.IsDuplicate&&!selection.AllowDuplicate)) continue;
            if(c.Kind=="Task") await SaveChecklistItemAsync(new ChecklistItemRecord { CaseId=caseId,Phase=c.Stage,Task=c.Title,DueDate=selection.DueDate,Status="Not Started",SourceType=$"Template:{c.TemplateId}",SourceKind="StageTemplate",SourceTemplateId=c.TemplateId,SourceTemplateVersion=c.TemplateVersion,SourceStage=c.Stage,GeneratedAt=now,GeneratedBy=_actor.AuditLabel,IsManual=false });
            else await SaveDeadlineAsync(new DeadlineItem { CaseId=caseId,Title=c.Title,DueDate=selection.DueDate,Status="Open",Severity=c.Severity??"normal",SourceType=$"Computed:{c.TemplateId}",SourceKind="DeadlineTemplate",SourceTemplateId=c.TemplateId,SourceTemplateVersion=c.TemplateVersion,SourceStage=c.Stage,GeneratedAt=now,GeneratedBy=_actor.AuditLabel,IsManual=false });
            added++;
        }
        if(added>0) await RecordActivityAsync(caseId,"TemplateBatchAdded",$"Added {added} task/deadline template item(s) after review",null);
        return added;
    }

    private async Task MigrateStructuredProvenanceV1Async(SqliteConnection connection)
    {
        const string flag = "structured_provenance_v1_complete";
        if (await GetAppSettingAsync(connection, flag) == "true") return;
        await ExecuteAsync(connection, """
            UPDATE deadlines SET
                source_kind=CASE WHEN source_type LIKE 'Computed:%' THEN 'DeadlineTemplate' WHEN source_type='Manual' THEN 'Manual' ELSE source_type END,
                source_template_id=CASE WHEN source_type LIKE 'Computed:%' THEN substr(source_type,10) ELSE NULL END,
                source_template_version=CASE WHEN source_type LIKE 'Computed:%' THEN 3 ELSE NULL END,
                generated_at=CASE WHEN source_type LIKE 'Computed:%' THEN created_at ELSE NULL END,
                generated_by=CASE WHEN source_type LIKE 'Computed:%' THEN 'Legacy migration' ELSE NULL END;
            UPDATE checklist_items SET
                source_kind=CASE WHEN source_type LIKE 'Template:%' THEN 'StageTemplate' WHEN source_type='Manual' THEN 'Manual' ELSE source_type END,
                source_template_id=CASE WHEN source_type LIKE 'Template:%' THEN substr(source_type,10) ELSE NULL END,
                source_template_version=CASE WHEN source_type LIKE 'Template:%' THEN 1 ELSE NULL END,
                source_stage=CASE WHEN source_type LIKE 'Template:%' THEN phase ELSE NULL END,
                generated_at=CASE WHEN source_type LIKE 'Template:%' THEN created_at ELSE NULL END,
                generated_by=CASE WHEN source_type LIKE 'Template:%' THEN 'Legacy migration' ELSE NULL END;
            """);
        await using var tx = connection.BeginTransaction();
        await SetAppSettingAsync(connection, tx, flag, "true");
        await tx.CommitAsync();
    }

    private async Task MigrateDedicatedDefermentsV1Async(SqliteConnection connection)
    {
        const string flag = "dedicated_deferments_v1_complete";
        if (await GetAppSettingAsync(connection, flag) == "true") return;
        var now = DateTime.UtcNow.ToString("O");
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE cases
            SET deferred_until = next_review_date,
                deferred_reason = CASE
                    WHEN next_action LIKE 'Deferred: %' THEN substr(next_action, 11)
                    ELSE NULL
                END,
                deferred_at = COALESCE(updated_at, @now),
                deferred_by = 'Legacy migration'
            WHERE COALESCE(deferred_until,'')=''
              AND COALESCE(next_review_date,'')<>''
              AND (next_action='Deferred' OR next_action LIKE 'Deferred: %');
            """;
        cmd.Parameters.AddWithValue("@now", now);
        await cmd.ExecuteNonQueryAsync();
        await using var tx = connection.BeginTransaction();
        await SetAppSettingAsync(connection, tx, flag, "true");
        await tx.CommitAsync();
    }

    public async Task<List<PublicationRecord>> GetPublicationRecordsAsync(long? caseId)
    {
        var list = new List<PublicationRecord>();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT case_id, first_publication_date, second_publication_date, publication_name,
                   marked_perfected, last_updated_at, last_updated_by
            FROM case_publications WHERE (@caseId IS NULL OR case_id=@caseId)
            """;
        cmd.Parameters.AddWithValue("@caseId", caseId is null ? DBNull.Value : caseId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new PublicationRecord
            {
                CaseId = reader.GetInt64(0),
                FirstPublicationDate = NormalizeDate(reader.IsDBNull(1) ? null : reader.GetString(1)),
                SecondPublicationDate = NormalizeDate(reader.IsDBNull(2) ? null : reader.GetString(2)),
                PublicationName = reader.IsDBNull(3) ? null : reader.GetString(3),
                MarkedPerfected = !reader.IsDBNull(4) && reader.GetInt64(4) == 1,
                LastUpdatedAt = reader.IsDBNull(5) ? null : reader.GetString(5),
                LastUpdatedBy = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }
        return list;
    }

    public async Task<PublicationRecord?> GetPublicationRecordAsync(long caseId) =>
        (await GetPublicationRecordsAsync(caseId)).FirstOrDefault();

    public async Task<PublicationRecord> SavePublicationRecordAsync(PublicationRecord model)
    {
        var first = ParseDate(model.FirstPublicationDate);
        var second = ParseDate(model.SecondPublicationDate);
        if (first is not null && second is not null && second < first)
            throw new InvalidOperationException("Second publication date cannot be earlier than the first publication date.");
        if ((first is not null || second is not null) && string.IsNullOrWhiteSpace(model.PublicationName) && !model.OverrideMissingPublicationName)
            throw new InvalidOperationException("Publication name is required when a publication date is entered. Confirm the override to save without it.");

        var saved = await WithWriteAsync(async (connection, tx) =>
        {
            var now = DateTime.UtcNow.ToString("O");
            var by = string.IsNullOrWhiteSpace(model.LastUpdatedBy) ? _actor.AuditLabel : model.LastUpdatedBy.Trim();
            var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO case_publications
                    (case_id, first_publication_date, second_publication_date, publication_name,
                     marked_perfected, last_updated_at, last_updated_by)
                VALUES (@case_id, @first, @second, @name, @perfected, @at, @by)
                ON CONFLICT(case_id) DO UPDATE SET
                    first_publication_date=excluded.first_publication_date,
                    second_publication_date=excluded.second_publication_date,
                    publication_name=excluded.publication_name,
                    marked_perfected=excluded.marked_perfected,
                    last_updated_at=excluded.last_updated_at,
                    last_updated_by=excluded.last_updated_by
                """;
            cmd.Parameters.AddWithValue("@case_id", model.CaseId);
            cmd.Parameters.AddWithValue("@first", DbValue(first?.ToString("yyyy-MM-dd")));
            cmd.Parameters.AddWithValue("@second", DbValue(second?.ToString("yyyy-MM-dd")));
            cmd.Parameters.AddWithValue("@name", DbValue(model.PublicationName?.Trim()));
            cmd.Parameters.AddWithValue("@perfected", model.MarkedPerfected ? 1 : 0);
            cmd.Parameters.AddWithValue("@at", now);
            cmd.Parameters.AddWithValue("@by", by);
            await cmd.ExecuteNonQueryAsync();
            model.FirstPublicationDate = first?.ToString("yyyy-MM-dd");
            model.SecondPublicationDate = second?.ToString("yyyy-MM-dd");
            model.LastUpdatedAt = now;
            model.LastUpdatedBy = by;
            return model;
        });
        await RecordActivityAsync(model.CaseId, "PublicationChanged",
            $"Publication updated; first {saved.FirstPublicationDate ?? "not set"}, second {saved.SecondPublicationDate ?? "not set"}, perfected {(saved.MarkedPerfected ? "yes" : "no")}", null);
        return saved;
    }

    private async Task MigrateCanonicalPublicationV1Async(SqliteConnection connection)
    {
        const string flag = "canonical_publication_v1_complete";
        if (await GetAppSettingAsync(connection, flag) == "true") return;

        var now = DateTime.UtcNow.ToString("O");
        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO case_publications
                (case_id, first_publication_date, second_publication_date, publication_name,
                 marked_perfected, last_updated_at, last_updated_by)
            SELECT c.id,
                   (SELECT pd.publication_date FROM publication_dates pd
                    WHERE pd.case_id=c.id AND COALESCE(pd.publication_date,'')<>''
                    ORDER BY pd.publication_date, pd.id LIMIT 1),
                   (SELECT pd.publication_date FROM publication_dates pd
                    WHERE pd.case_id=c.id AND COALESCE(pd.publication_date,'')<>''
                    ORDER BY pd.publication_date, pd.id LIMIT 1 OFFSET 1),
                   (SELECT pd.newspaper FROM publication_dates pd
                    WHERE pd.case_id=c.id AND COALESCE(pd.newspaper,'')<>''
                    ORDER BY COALESCE(pd.publication_date,'9999-12-31'), pd.id LIMIT 1),
                   CASE WHEN EXISTS (SELECT 1 FROM publication_dates pd
                                    WHERE pd.case_id=c.id AND (pd.proof_filed=1 OR pd.service_resolved=1))
                        THEN 1 ELSE 0 END,
                   @now, 'Legacy migration'
            FROM cases c
            WHERE EXISTS (SELECT 1 FROM publication_dates pd WHERE pd.case_id=c.id);
            """;
        cmd.Parameters.AddWithValue("@now", now);
        await cmd.ExecuteNonQueryAsync();
        await using var tx = connection.BeginTransaction();
        await SetAppSettingAsync(connection, tx, flag, "true");
        await tx.CommitAsync();
    }

    // One-time conversion of risk_analyses.rows_json from the old fixed-5-row
    // Dictionary<string, RiskAnalysisRowInput> shape to the new user-extensible
    // List<RiskAnalysisRowInput> shape (offer-maker + date + per-row split flag). Row names are
    // mapped to an inferred OfferMaker and IncludeSplit is set for the 3 rows that previously had
    // a hardcoded split - see RiskAnalysisEngine's doc comment for the row semantics this preserves.
    private static readonly (string Key, string Label, string OfferMaker, bool IncludeSplit)[] LegacyRiskAnalysisRowOrder =
    [
        ("LandownerOpinionOfValue", "Landowner's Opinion of Value", "Landowner", false),
        ("LandownerAppraisal", "Landowner's Appraisal", "Landowner", false),
        ("AshcFirstOffer", "ASHC First Offer", "ASHC", true),
        ("AshcCounteroffer", "ASHC Counteroffer", "ASHC", true),
        ("LandownerCounteroffer", "Landowner's Counteroffer", "Landowner", true),
    ];

    private async Task MigrateRiskAnalysisRowsToListV1Async(SqliteConnection connection)
    {
        const string flag = "risk_analysis_rows_to_list_v1_complete";
        if (await GetAppSettingAsync(connection, flag) == "true")
        {
            return;
        }

        var selectCmd = connection.CreateCommand();
        selectCmd.CommandText = "SELECT id, rows_json FROM risk_analyses";
        var toMigrate = new List<(long Id, string RowsJson)>();
        await using (var reader = await selectCmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                toMigrate.Add((reader.GetInt64(0), reader.GetString(1)));
            }
        }

        await using var tx = connection.BeginTransaction();
        foreach (var (id, rowsJson) in toMigrate)
        {
            Dictionary<string, RiskAnalysisRowInput>? legacyRows;
            try
            {
                legacyRows = JsonSerializer.Deserialize<Dictionary<string, RiskAnalysisRowInput>>(rowsJson);
            }
            catch (JsonException)
            {
                // Already list-shaped (a fresh install with no legacy data) - nothing to migrate.
                continue;
            }

            if (legacyRows is null)
            {
                continue;
            }

            var migratedRows = new List<RiskAnalysisRowInput>();
            foreach (var (key, label, offerMaker, includeSplit) in LegacyRiskAnalysisRowOrder)
            {
                if (!legacyRows.TryGetValue(key, out var legacyRow))
                {
                    continue;
                }

                migratedRows.Add(new RiskAnalysisRowInput
                {
                    RowKey = key,
                    Label = label,
                    OfferMaker = offerMaker,
                    IncludeSplit = includeSplit,
                    JustCompensation = legacyRow.JustCompensation,
                    LandownerFeesCosts = legacyRow.LandownerFeesCosts,
                    AshcCosts = legacyRow.AshcCosts,
                    HourlyFeesRisk = legacyRow.HourlyFeesRisk,
                });
            }

            var updateCmd = connection.CreateCommand();
            updateCmd.Transaction = tx;
            updateCmd.CommandText = "UPDATE risk_analyses SET rows_json=@rows_json WHERE id=@id";
            updateCmd.Parameters.AddWithValue("@rows_json", JsonSerializer.Serialize(migratedRows));
            updateCmd.Parameters.AddWithValue("@id", id);
            await updateCmd.ExecuteNonQueryAsync();
        }

        await SetAppSettingAsync(connection, tx, flag, "true");
        await tx.CommitAsync();
    }

    // One-time backfill so existing cases don't all show as "no meaningful activity ever" the
    // moment the new momentum logic ships: seeds last_meaningful_activity_date from the same
    // MAX(updated_at) proxy CaseAttentionEngine already uses, and logs one synthetic activity_log
    // row per case so AttorneyDashboardEngine has something to point to. Real activity recording
    // (RecordActivityAsync at real mutation points) takes over for everything after this point.
    private async Task MigrateBackfillMeaningfulActivityV1Async(SqliteConnection connection)
    {
        const string flag = "backfill_meaningful_activity_v1_complete";
        if (await GetAppSettingAsync(connection, flag) == "true")
        {
            return;
        }

        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id,
                   COALESCE((
                       SELECT MAX(updated_at) FROM (
                           SELECT case_id, updated_at FROM checklist_items WHERE case_id = cases.id
                           UNION ALL
                           SELECT case_id, updated_at FROM deadlines WHERE case_id = cases.id
                           UNION ALL
                           SELECT case_id, updated_at FROM discovery_tracking WHERE case_id = cases.id
                       )
                   ), updated_at, created_at)
            FROM cases
            """;
        var backfill = new List<(long CaseId, string? LastActivity)>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                backfill.Add((reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
            }
        }

        var now = DateTime.UtcNow.ToString("O");
        await using var tx = connection.BeginTransaction();
        foreach (var (caseId, lastActivity) in backfill)
        {
            var occurredAt = lastActivity ?? now;
            var updateCmd = connection.CreateCommand();
            updateCmd.Transaction = tx;
            updateCmd.CommandText = "UPDATE cases SET last_meaningful_activity_date=@occurredAt WHERE id=@id";
            updateCmd.Parameters.AddWithValue("@occurredAt", occurredAt);
            updateCmd.Parameters.AddWithValue("@id", caseId);
            await updateCmd.ExecuteNonQueryAsync();

            var insertCmd = connection.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText = """
                INSERT INTO activity_log (case_id, activity_type, is_meaningful, occurred_at, notes, created_at)
                VALUES (@caseId, 'AttorneyStrategyDecisionRecorded', 1, @occurredAt, 'Backfilled from prior activity history.', @now)
                """;
            insertCmd.Parameters.AddWithValue("@caseId", caseId);
            insertCmd.Parameters.AddWithValue("@occurredAt", occurredAt);
            insertCmd.Parameters.AddWithValue("@now", now);
            await insertCmd.ExecuteNonQueryAsync();
        }

        await SetAppSettingAsync(connection, tx, flag, "true");
        await tx.CommitAsync();
    }

    // Case Type (Standard/Friendly) is retired in favor of a single Track field
    // (Contested/Settlement/Default/Friendly) - Friendly absorbs the old CaseType='Friendly'
    // meaning. The 9-stage list also collapses to 5 high-level stages here. Must run after
    // MigrateLegacyStageNamesAsync so cases.stage is already in the 9-stage vocabulary this
    // mapping expects. The case_type column is left in place (unread) rather than dropped.
    private static readonly Dictionary<string, string> StageCollapseMap = new(StringComparer.Ordinal)
    {
        ["Pre-Suit / Intake"] = "Intake & Filing",
        ["Pleadings"] = "Intake & Filing",
        ["Service"] = "Service",
        ["Discovery"] = "Discovery & Evaluation",
        ["Post-Discovery / Settlement Evaluation"] = "Discovery & Evaluation",
        ["Scheduling Order Received"] = "Trial Track",
        ["Trial Preparation"] = "Trial Track",
        ["Trial"] = "Trial Track",
        ["Post-Trial"] = "Resolved",
    };

    private async Task MigrateStageTrackUnificationV1Async(SqliteConnection connection)
    {
        if (await GetAppSettingAsync(connection, "stage_track_unification_v1_complete") == "true")
        {
            return;
        }

        var flagged = new List<string>();
        var findConflicts = connection.CreateCommand();
        findConflicts.CommandText = "SELECT case_number FROM cases WHERE case_type = 'Friendly' AND track <> 'Contested'";
        await using (var reader = await findConflicts.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                flagged.Add($"{reader.GetString(0)} (had case_type='Friendly' and track<>'Contested' - track set to 'Friendly', review if that's correct)");
            }
        }

        var trackCmd = connection.CreateCommand();
        trackCmd.CommandText = "UPDATE cases SET track = 'Friendly' WHERE case_type = 'Friendly'";
        await trackCmd.ExecuteNonQueryAsync();

        foreach (var (oldStage, newStage) in StageCollapseMap)
        {
            var caseCmd = connection.CreateCommand();
            caseCmd.CommandText = "UPDATE cases SET stage = @newStage WHERE stage = @oldStage";
            caseCmd.Parameters.AddWithValue("@newStage", newStage);
            caseCmd.Parameters.AddWithValue("@oldStage", oldStage);
            await caseCmd.ExecuteNonQueryAsync();

            var templateCmd = connection.CreateCommand();
            templateCmd.CommandText = "UPDATE checklist_templates SET stage = @newStage WHERE stage = @oldStage";
            templateCmd.Parameters.AddWithValue("@newStage", newStage);
            templateCmd.Parameters.AddWithValue("@oldStage", oldStage);
            await templateCmd.ExecuteNonQueryAsync();

            // checklist_template_items.phase and checklist_items.phase are seeded directly from
            // the stage name (see SeedChecklistTemplatesAsync's `phase = seed.Stage`), so they
            // carry the same old-vocabulary values and need the identical rename.
            var templateItemCmd = connection.CreateCommand();
            templateItemCmd.CommandText = "UPDATE checklist_template_items SET phase = @newStage WHERE phase = @oldStage";
            templateItemCmd.Parameters.AddWithValue("@newStage", newStage);
            templateItemCmd.Parameters.AddWithValue("@oldStage", oldStage);
            await templateItemCmd.ExecuteNonQueryAsync();

            var itemCmd = connection.CreateCommand();
            itemCmd.CommandText = "UPDATE checklist_items SET phase = @newStage WHERE phase = @oldStage";
            itemCmd.Parameters.AddWithValue("@newStage", newStage);
            itemCmd.Parameters.AddWithValue("@oldStage", oldStage);
            await itemCmd.ExecuteNonQueryAsync();
        }

        await using var tx = connection.BeginTransaction();
        await SetAppSettingAsync(connection, tx, "stage_track_unification_v1_complete", "true");
        if (flagged.Count > 0)
        {
            await SetAppSettingAsync(connection, tx, "stage_track_unification_v1_review", string.Join("; ", flagged));
        }

        await tx.CommitAsync();
    }

    // Risk Analysis moved from a named-scenario model (multiple rows per case_id) to a
    // single live ledger per case (case_id UNIQUE), matching how the real workbook is
    // used - one sheet, periodically updated. Confirmed no production data existed under
    // the old shape at the time of this change, so this drops and recreates the table
    // rather than attempting a row-collapsing migration.
    private async Task MigrateRiskAnalysesToSingleRecordAsync(SqliteConnection connection)
    {
        if (await GetAppSettingAsync(connection, "risk_analysis_single_record_v1_complete") == "true")
        {
            return;
        }

        var dropCmd = connection.CreateCommand();
        dropCmd.CommandText = "DROP TABLE IF EXISTS risk_analyses";
        await dropCmd.ExecuteNonQueryAsync();

        var createCmd = connection.CreateCommand();
        createCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS risk_analyses (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                case_id INTEGER NOT NULL UNIQUE,
                narrative TEXT,
                rows_json TEXT NOT NULL,
                analysis_date TEXT,
                interest_rate REAL NOT NULL DEFAULT 0.06,
                contingency_fee_percent REAL NOT NULL DEFAULT 0.30,
                created_at TEXT,
                updated_at TEXT
            );
            """;
        await createCmd.ExecuteNonQueryAsync();

        await using var tx = connection.BeginTransaction();
        await SetAppSettingAsync(connection, tx, "risk_analysis_single_record_v1_complete", "true");
        await tx.CommitAsync();
    }

    // Best-guess mapping from the original 7-value stage list to the 9-stage
    // process model. "Complaint Filed" and "Pre-Trial" are genuinely ambiguous
    // (the old vocabulary bundled steps the new one splits apart) so cases
    // remapped from those two are logged for attorney review rather than
    // assumed correct.
    private static readonly Dictionary<string, string> LegacyStageMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Pre-Filing"] = "Pre-Suit / Intake",
        ["Complaint Filed"] = "Service",
        ["Discovery"] = "Discovery",
        ["Settlement/Mediation"] = "Post-Discovery / Settlement Evaluation",
        ["Pre-Trial"] = "Trial Preparation",
        ["Trial"] = "Trial",
        ["Judgment"] = "Post-Trial"
    };

    private static readonly HashSet<string> AmbiguousLegacyStages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Complaint Filed", "Pre-Trial"
    };

    private async Task MigrateLegacyStageNamesAsync(SqliteConnection connection)
    {
        if (await GetAppSettingAsync(connection, "stage_migration_v2_complete") == "true")
        {
            return;
        }

        var flagged = new List<string>();
        foreach (var (oldStage, newStage) in LegacyStageMap)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE cases SET stage = @newStage WHERE stage = @oldStage";
            cmd.Parameters.AddWithValue("@newStage", newStage);
            cmd.Parameters.AddWithValue("@oldStage", oldStage);
            var affected = await cmd.ExecuteNonQueryAsync();
            if (affected > 0 && AmbiguousLegacyStages.Contains(oldStage))
            {
                var listCmd = connection.CreateCommand();
                listCmd.CommandText = "SELECT case_number FROM cases WHERE stage = @newStage";
                listCmd.Parameters.AddWithValue("@newStage", newStage);
                await using var reader = await listCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    flagged.Add($"{reader.GetString(0)} (was '{oldStage}', auto-mapped to '{newStage}')");
                }
            }
        }

        await using var tx = connection.BeginTransaction();
        await SetAppSettingAsync(connection, tx, "stage_migration_v2_complete", "true");
        if (flagged.Count > 0)
        {
            await SetAppSettingAsync(connection, tx, "stage_migration_v2_review", string.Join("; ", flagged));
        }

        await tx.CommitAsync();
    }

    private static readonly (string Name, string Description, string Category)[] IssueTagCatalog =
    [
        ("Partial Taking", "Partial taking; remainder and damages analysis", "Valuation"),
        ("Full Taking", "Entire parcel acquired; no remainder", "Valuation"),
        ("Easement Only", "Permanent easement taking without fee acquisition", "Valuation"),
        ("Temporary Construction Easement", "TCE valuation and duration tracking", "Valuation"),
        ("Severance Damages", "Potential severance damages review", "Valuation"),
        ("Access / Change of Access", "Access control or change of access affecting remainder value", "Valuation"),
        ("Drainage", "Drainage changes or damage claims", "Valuation"),
        ("Landlocked Remainder", "Remainder left without legal access", "Valuation"),
        ("Minerals", "Mineral interests affected by the taking", "Valuation"),
        ("Timber", "Merchantable timber valuation", "Valuation"),
        ("Billboard / Sign", "Outdoor advertising structure on the parcel", "Valuation"),
        ("Relocation - Residential", "Occupied residence; relocation assistance implications", "Parties"),
        ("Relocation - Business", "Operating business; relocation and disruption issues", "Parties"),
        ("Leasehold / Tenant Interest", "Tenant or leasehold interest requiring apportionment", "Parties"),
        ("Estate / Probate", "Deceased owner; heirs or estate administration involved", "Parties"),
        ("Unknown Heirs / Owners", "Unknown or unlocatable owners; warning order and publication service", "Procedure"),
        ("Lienholder / Mortgage", "Mortgage or lien claims against the compensation", "Parties"),
        ("Tax Delinquent / COSL", "Tax-delinquent parcel; Commissioner of State Lands involved", "Parties"),
        ("Utility Conflict", "Utility facilities or relocation conflicts on the parcel", "Procedure"),
        ("Contested Right-to-Take", "Landowner challenges authority or necessity of the taking", "Procedure"),
        ("Publication Service", "Publication and service follow-up", "Procedure"),
        ("Trial Likely", "Case likely to require trial prep", "Trial")
    ];

    private async Task EnsureIssueTagCatalogAsync(SqliteConnection connection)
    {
        foreach (var (name, description, category) in IssueTagCatalog)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO issue_tags (name, description, category)
                SELECT @name, @description, @category
                WHERE NOT EXISTS (SELECT 1 FROM issue_tags WHERE name = @name)
                """;
            cmd.Parameters.AddWithValue("@name", name);
            cmd.Parameters.AddWithValue("@description", description);
            cmd.Parameters.AddWithValue("@category", category);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private sealed record TemplateSeed(string Name, string TriggerType, string? Stage, string? IssueTagName, string Track, string[] Tasks);

    // Stage templates seeded from the office condemnation checklist reference.
    // Track "Any" applies to all cases; specific tracks (Settlement/Contested/Default/Friendly)
    // narrow to only that track's cases.
    private static readonly TemplateSeed[] TemplateSeeds =
    [
        new("Pre-Suit / Intake", "Stage", "Pipeline", null, "Any",
        [
            "File received in Legal; title attorney prepares condemnation pre-file sheet",
            "Deputy Chief Counsel reviews and assigns to staff attorney; log in Lawtoolbox",
            "Review Condemnation Memo, Tract File, and Negotiator's Notes: title issues? why didn't it settle with Acquisitions? is an attorney already involved?",
            "Review construction plans/ROW maps for design peculiarities",
            "Confirm appraisal is a full appraisal, not just an estimate",
            "Check appraisal date relative to date of taking; flag for update if stale",
            "Confirm appraiser is staff or a consultant and available for trial",
            "Check comps: if 5+ years old as of date of taking, flag to ask appraiser about updating"
        ]),
        new("Pleadings", "Stage", "Pipeline", null, "Any",
        [
            "Legal assistant prepares initial pleadings from attorney's packet",
            "Attorney reviews pleadings",
            "Deputy Chief Counsel second review",
            "Confirm filing fee, summons, and just-compensation deposit amount",
            "Chief Counsel signs pleadings",
            "Director signs Declaration of Taking",
            "File lawsuit",
            "Make deposit"
        ]),
        new("Service - Core", "Stage", "Filed / Service Pending", null, "Any",
        [
            "Service deadline: 120 days from filing — calendar this date (reminder at 60 days)",
            "Email appraiser (cc appraisal section division head) to update appraisal as of date of taking",
            "Place warning order in paper",
            "If registered-mail service is returned: request Sheriff service",
            "If Sheriff service is unsuccessful: file Motion for Specific Warning Order",
            "Watch alert: if service/warning order is incomplete by day 90, file Motion for Extension of Time to Serve",
            "Visit property with the Real Estate agent/negotiator"
        ]),
        // The per-request tracking tasks that used to live here (serve/contact/extend/good-faith
        // letter/motion to compel) are now covered per-request by the Discovery tab's request
        // cards (status, dates, and escalation note), so they're intentionally not generated as
        // generic checklist reminders anymore. Only the case-level tasks that aren't tied to any
        // single discovery request stay on the checklist.
        new("Discovery - Core", "Stage", "Active Litigation", null, "Any",
        [
            "Review landowner's appraisal against checklist: method used, subject vs. landowner comps vs. our comps, utilities match, flood plain, adjustments (time/location/size), damage to building",
            "Send landowner's appraisal to our appraiser for review and rebuttal notes",
            "Schedule depositions: landowner, their appraiser, and other identified witnesses"
        ]),
        new("Post-Discovery / Settlement Evaluation - Core", "Stage", "Active Litigation", null, "Any",
        [
            "If landowner raises settlement: request their offer",
            "Prepare Risk Analysis and discuss with Chief Counsel",
            "Outline weaknesses in appraisal, witnesses, and key issues"
        ]),
        new("Post-Discovery - Settlement Path", "Stage", "Settlement Pending", null, "Settlement",
        [
            "Prepare consent judgment and settlement justification memo",
            "Route settlement justification memo to Chief Counsel for approval"
        ]),
        new("Post-Discovery - Contested Path", "Stage", "Trial Preparation", null, "Contested",
        [
            "Request trial date"
        ]),
        new("Post-Discovery - Default Path", "Stage", "Active Litigation", null, "Default",
        [
            "If no answer filed ~6 months post-service: prepare default-style judgment noting service obtained, no answer or appearance",
            "Note: funds stay in the court registry and escheat to the state after 1 year unclaimed"
        ]),
        new("Scheduling Order Received - Core", "Stage", "Trial Preparation", null, "Any",
        [
            "Calendar all court deadlines with reminders 30-60 days out, as appropriate to each deadline",
            "30 days before trial: request jury questionnaire",
            "Track pretrial motions, witness/exhibit list exchange, and jury instructions per the court's schedule"
        ]),
        new("Scheduling Order - Mediation", "Stage", "Trial Preparation", null, "Any",
        [
            "If mediation is ordered: get the mediator approved by Chief Counsel",
            "If mediation is ordered: review Risk Analysis and set authorized settlement limits before the mediation date"
        ]),
        new("Trial Preparation - Core", "Stage", "Trial Preparation", null, "Any",
        [
            "Order large aerial/enlarged exhibits",
            "Evaluate and file any Motions in Limine",
            "Build trial notebook",
            "Pre-label exhibits, don't number until offered in court: summary sheet, comps map, ROW map, construction plans, aerial with ROW lines, subject photos, appraiser CV",
            "Note: the full appraisal cannot come into evidence (hearsay) — only specific parts via appraiser testimony",
            "Request jury questionnaires for voir dire",
            "Prepare and exchange jury instructions; flag any non-agreed instructions to proffer separately"
        ]),
        new("Trial - Core", "Stage", "Trial Preparation", null, "Any",
        [
            "Argue pretrial motions",
            "Conduct voir dire; confirm 12-person jury impaneled (Ark. Code Ann. § 18-15-103(9))",
            "Deliver opening statements (landowner goes first — they carry the burden of proof)",
            "Defendant's case-in-chief",
            "Address directed verdict motion",
            "Plaintiff's case",
            "Address renewed directed verdict motion",
            "Finalize jury instructions",
            "Deliver closing argument",
            "Receive verdict"
        ]),
        new("Post-Trial - Core", "Stage", "Resolved / Closed", null, "Any",
        [
            "Prepare judgment (include everything ARDOT needs) and a trial report",
            "Order additional deposit check if the judgment requires it",
            "File judgment (and any additional funds)",
            "Evaluate appealable issues; file appeal if warranted",
            "If landowner moves for attorney's fees: confirm the Ark. Code Ann. § 27-67-317(b) threshold was actually met — a jury verdict (not a negotiated settlement) exceeding the deposit by 20% or more — before treating the claimed fee amount as owed",
            "Meet with Deputy Chief Counsel / Chief Legal Counsel to debrief",
            "Close case; notify ROW"
        ]),
        // Issue-tag add-ons (unscoped by stage: generated whenever the tag is on the case, at any stage/track).
        new("Tag: Partial Taking", "IssueTag", null, "Partial Taking", "Any",
        [
            "Analyze before-and-after values for remainder damages",
            "Confirm taking and remainder are clearly delineated on the plat",
            "Evaluate cost-to-cure options for remainder impacts"
        ]),
        new("Tag: Full Taking", "IssueTag", null, "Full Taking", "Any",
        [
            "Confirm whole-parcel valuation with no remainder issues",
            "Verify relocation obligations for any occupants"
        ]),
        new("Tag: Easement Only", "IssueTag", null, "Easement Only", "Any",
        [
            "Confirm easement scope and duration language matches construction plans",
            "Value easement impact on the servient estate"
        ]),
        new("Tag: Temporary Construction Easement", "IssueTag", null, "Temporary Construction Easement", "Any",
        [
            "Confirm TCE duration and restoration obligations",
            "Calculate TCE rental value for the term"
        ]),
        new("Tag: Severance Damages", "IssueTag", null, "Severance Damages", "Any",
        [
            "Develop severance damages analysis",
            "Prepare rebuttal to landowner severance claims"
        ]),
        new("Tag: Access / Change of Access", "IssueTag", null, "Access / Change of Access", "Any",
        [
            "Analyze pre- and post-taking access and circuity impacts",
            "Determine whether access change is compensable or non-compensable police power"
        ]),
        new("Tag: Drainage", "IssueTag", null, "Drainage", "Any",
        [
            "Document pre-existing drainage conditions",
            "Evaluate drainage damage claims and coordinate engineering response"
        ]),
        new("Tag: Landlocked Remainder", "IssueTag", null, "Landlocked Remainder", "Any",
        [
            "Confirm whether remainder retains legal access",
            "Evaluate acquiring remainder versus providing access easement"
        ]),
        new("Tag: Minerals", "IssueTag", null, "Minerals", "Any",
        [
            "Identify severed mineral interests and owners",
            "Join mineral owners as parties if interests are compensable"
        ]),
        new("Tag: Timber", "IssueTag", null, "Timber", "Any",
        [
            "Obtain timber cruise/valuation",
            "Address timber removal rights before possession"
        ]),
        new("Tag: Billboard / Sign", "IssueTag", null, "Billboard / Sign", "Any",
        [
            "Identify sign owner and lease terms (separate compensable interest)",
            "Handle sign relocation/removal compensation"
        ]),
        new("Tag: Relocation - Residential", "IssueTag", null, "Relocation - Residential", "Any",
        [
            "Coordinate residential relocation benefits with ROW",
            "Confirm occupancy and displacement dates"
        ]),
        new("Tag: Relocation - Business", "IssueTag", null, "Relocation - Business", "Any",
        [
            "Coordinate business relocation benefits with ROW",
            "Evaluate admissibility of business disruption claims"
        ]),
        new("Tag: Leasehold / Tenant Interest", "IssueTag", null, "Leasehold / Tenant Interest", "Any",
        [
            "Join tenants/leaseholders as defendants",
            "Apportion award between fee and leasehold interests"
        ]),
        new("Tag: Estate / Probate", "IssueTag", null, "Estate / Probate", "Any",
        [
            "Identify heirs and confirm estate administration status",
            "Serve heirs or publish warning order for unknown heirs"
        ]),
        new("Tag: Unknown Heirs / Owners", "IssueTag", null, "Unknown Heirs / Owners", "Any",
        [
            "Draft affidavit and warning order for unknown/unlocatable owners",
            "Publish warning order and file proof of publication",
            "Consider attorney ad litem appointment where required"
        ]),
        new("Tag: Lienholder / Mortgage", "IssueTag", null, "Lienholder / Mortgage", "Any",
        [
            "Name lienholders/mortgagees as defendants",
            "Track lien releases or apportionment from the award"
        ]),
        new("Tag: Tax Delinquent / COSL", "IssueTag", null, "Tax Delinquent / COSL", "Any",
        [
            "Confirm COSL certification status of the parcel",
            "Name Commissioner of State Lands as a party if certified",
            "Resolve delinquent taxes from the deposited funds"
        ]),
        new("Tag: Utility Conflict", "IssueTag", null, "Utility Conflict", "Any",
        [
            "Identify utility facilities within the taking",
            "Coordinate utility relocation agreements"
        ]),
        new("Tag: Contested Right-to-Take", "IssueTag", null, "Contested Right-to-Take", "Any",
        [
            "Brief authority and necessity issues",
            "Prepare for right-to-take hearing"
        ]),
        new("Tag: Publication Service", "IssueTag", null, "Publication Service", "Any",
        [
            "Track publication dates and proof of publication filings"
        ]),
        new("Tag: Trial Likely", "IssueTag", null, "Trial Likely", "Any",
        [
            "Begin early trial theme development",
            "Confirm expert availability for the anticipated trial window"
        ])
    ];

    // Bump this when TemplateSeeds content changes materially so existing installs re-seed.
    // Safe because template rows only carry static task text; the per-case checklist_items
    // rows generated from them are separate and are never deleted by a re-seed.
    private const string ChecklistTemplateVersion = "8";

    private async Task<bool> SeedChecklistTemplatesAsync(SqliteConnection connection)
    {
        var storedVersion = await GetAppSettingAsync(connection, "checklist_template_version");
        if (storedVersion == ChecklistTemplateVersion)
        {
            return false;
        }

        // Additive/idempotent, not wipe-and-reinsert: a checklist template editor now lets
        // users customize these rows, and a future ChecklistTemplateVersion bump must not
        // silently destroy that. Existing templates (matched by name) and existing items
        // (matched by template_id+sort_order) are left untouched; only seed rows genuinely
        // missing get inserted, so hand edits and custom templates survive version bumps.
        var now = DateTime.UtcNow.ToString("O");
        foreach (var seed in TemplateSeeds)
        {
            var findTemplate = connection.CreateCommand();
            findTemplate.CommandText = "SELECT id FROM checklist_templates WHERE name = @name";
            findTemplate.Parameters.AddWithValue("@name", seed.Name);
            var existingTemplateId = await findTemplate.ExecuteScalarAsync();

            long templateId;
            if (existingTemplateId is null)
            {
                var insertTemplate = connection.CreateCommand();
                insertTemplate.CommandText = """
                    INSERT INTO checklist_templates (name, trigger_type, stage, issue_tag_name, track, active, created_at, updated_at)
                    VALUES (@name, @trigger_type, @stage, @issue_tag_name, @track, 1, @now, @now);
                    SELECT last_insert_rowid();
                    """;
                insertTemplate.Parameters.AddWithValue("@name", seed.Name);
                insertTemplate.Parameters.AddWithValue("@trigger_type", seed.TriggerType);
                insertTemplate.Parameters.AddWithValue("@stage", DbValue(seed.Stage));
                insertTemplate.Parameters.AddWithValue("@issue_tag_name", DbValue(seed.IssueTagName));
                insertTemplate.Parameters.AddWithValue("@track", seed.Track);
                insertTemplate.Parameters.AddWithValue("@now", now);
                templateId = Convert.ToInt64(await insertTemplate.ExecuteScalarAsync());
            }
            else
            {
                templateId = Convert.ToInt64(existingTemplateId);
            }

            var phase = seed.TriggerType == "Stage" ? seed.Stage : seed.IssueTagName;
            for (var i = 0; i < seed.Tasks.Length; i++)
            {
                var findItem = connection.CreateCommand();
                findItem.CommandText = "SELECT COUNT(*) FROM checklist_template_items WHERE template_id = @template_id AND sort_order = @sort_order";
                findItem.Parameters.AddWithValue("@template_id", templateId);
                findItem.Parameters.AddWithValue("@sort_order", i);
                var itemExists = Convert.ToInt32(await findItem.ExecuteScalarAsync()) > 0;
                if (itemExists)
                {
                    continue;
                }

                var insertItem = connection.CreateCommand();
                insertItem.CommandText = """
                    INSERT INTO checklist_template_items (template_id, task, phase, sort_order, due_offset_days, created_at, updated_at)
                    VALUES (@template_id, @task, @phase, @sort_order, NULL, @now, @now)
                    """;
                insertItem.Parameters.AddWithValue("@template_id", templateId);
                insertItem.Parameters.AddWithValue("@task", seed.Tasks[i]);
                insertItem.Parameters.AddWithValue("@phase", DbValue(phase));
                insertItem.Parameters.AddWithValue("@sort_order", i);
                insertItem.Parameters.AddWithValue("@now", now);
                await insertItem.ExecuteNonQueryAsync();
            }
        }

        await using var tx = connection.BeginTransaction();
        await SetAppSettingAsync(connection, tx, "checklist_template_version", ChecklistTemplateVersion);
        await tx.CommitAsync();
        return true;
    }

    private async Task BackfillChecklistsForCasesWithStageAsync(SqliteConnection connection)
    {
        var ids = new List<long>();
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id FROM cases WHERE COALESCE(case_status, '') NOT IN ('', 'Triage', 'Pipeline')";
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                ids.Add(reader.GetInt64(0));
            }
        }

        await using var tx = connection.BeginTransaction();
        foreach (var id in ids)
        {
            await GenerateChecklistForCaseAsync(connection, tx, id);
        }

        await tx.CommitAsync();
    }

    // Deadline templates compute real deadlines rows from a case's own trigger dates
    // (filing date, trial date) plus a day offset. Severity tiers: "soft" (routine
    // reminder), "urgent" (materially more pressing), "critical" (hard deadline).
    private sealed record DeadlineTemplateSeed(string Name, string TriggerField, int OffsetDays, string Title, string Severity, string Track);

    private const string DeadlineTemplateVersion = "4";

    private static readonly DeadlineTemplateSeed[] DeadlineTemplateSeeds =
    [
        new("Service - 60 Day Check-In", "filing_date", 60,
            "Service status check-in — evaluate whether a Motion for Extension of Time to Serve will be needed before day 120", "soft", "Any"),
        new("Service - 90 Day Watch Alert", "filing_date", 90,
            "Watch alert: file Motion for Extension of Time to Serve if service/warning order is not yet complete", "urgent", "Any"),
        new("Service - 120 Day Deadline", "filing_date", 120,
            "120-day service deadline", "critical", "Any"),
        new("Trial - Jury Questionnaire Request", "trial_date", -30,
            "Request jury questionnaire (30 days before trial)", "soft", "Any"),
        new("No-Answer Default Judgment Checkpoint", "service_perfected_date", 180,
            "Check whether an answer or appearance has been filed (~6 months after service). If not, move this case to the Default track and prepare a default-style judgment.", "urgent", "Any")
    ];

    private async Task<bool> SeedDeadlineTemplatesAsync(SqliteConnection connection)
    {
        var storedVersion = await GetAppSettingAsync(connection, "deadline_template_version");
        if (storedVersion == DeadlineTemplateVersion)
        {
            return false;
        }

        await ExecuteAsync(connection, "DELETE FROM deadline_templates");

        var now = DateTime.UtcNow.ToString("O");
        foreach (var seed in DeadlineTemplateSeeds)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO deadline_templates (name, trigger_field, offset_days, title, severity, track, active, created_at, updated_at)
                VALUES (@name, @trigger_field, @offset_days, @title, @severity, @track, 1, @now, @now)
                """;
            cmd.Parameters.AddWithValue("@name", seed.Name);
            cmd.Parameters.AddWithValue("@trigger_field", seed.TriggerField);
            cmd.Parameters.AddWithValue("@offset_days", seed.OffsetDays);
            cmd.Parameters.AddWithValue("@title", seed.Title);
            cmd.Parameters.AddWithValue("@severity", seed.Severity);
            cmd.Parameters.AddWithValue("@track", seed.Track);
            cmd.Parameters.AddWithValue("@now", now);
            await cmd.ExecuteNonQueryAsync();
        }

        await using var tx = connection.BeginTransaction();
        await SetAppSettingAsync(connection, tx, "deadline_template_version", DeadlineTemplateVersion);
        await tx.CommitAsync();
        return true;
    }

    private async Task BackfillDeadlinesForCasesAsync(SqliteConnection connection)
    {
        var ids = new List<long>();
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id FROM cases WHERE COALESCE(filing_date, '') <> '' OR COALESCE(trial_date, '') <> ''";
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                ids.Add(reader.GetInt64(0));
            }
        }

        await using var tx = connection.BeginTransaction();
        foreach (var id in ids)
        {
            await GenerateDeadlinesForCaseAsync(connection, tx, id);
        }

        await tx.CommitAsync();
    }

    public async Task<(int Added, int Updated)> GenerateDeadlinesAsync(long caseId)
    {
        return await WithWriteAsync(async (connection, tx) =>
        {
            var result = await GenerateDeadlinesForCaseAsync(connection, tx, caseId);
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Generated deadlines for case {caseId} at {DateTime.Now:G}: {result.Added} added, {result.Updated} updated");
            return result;
        });
    }

    private async Task<(int Added, int Updated)> GenerateDeadlinesForCaseAsync(SqliteConnection connection, SqliteTransaction tx, long caseId)
    {
        var caseCmd = connection.CreateCommand();
        caseCmd.Transaction = tx;
        caseCmd.CommandText = """
            SELECT filing_date, trial_date, service_perfected, service_perfected_date,
                   COALESCE(status,''), COALESCE(stage,'')
            FROM cases WHERE id=@id
            """;
        caseCmd.Parameters.AddWithValue("@id", caseId);
        DateOnly? filingDate;
        DateOnly? trialDate;
        bool servicePerfected;
        DateOnly? servicePerfectedDate;
        string caseStatus;
        string caseStage;
        await using (var caseReader = await caseCmd.ExecuteReaderAsync())
        {
            if (!await caseReader.ReadAsync())
            {
                return (0, 0);
            }

            filingDate = ParseDate(caseReader.IsDBNull(0) ? null : caseReader.GetString(0));
            trialDate = ParseDate(caseReader.IsDBNull(1) ? null : caseReader.GetString(1));
            servicePerfected = !caseReader.IsDBNull(2) && caseReader.GetInt64(2) == 1;
            servicePerfectedDate = ParseDate(caseReader.IsDBNull(3) ? null : caseReader.GetString(3));
            caseStatus = caseReader.GetString(4);
            caseStage = caseReader.GetString(5);
        }

        // Triage cases (freshly imported, not yet confirmed through the triage wizard) generate
        // no deadlines at all - the wizard backfills historical dates first, then activation
        // triggers generation with the correct anchors. This is the root fix for historical
        // imports spawning "31 days after filing" reminders decades late.
        if (caseStatus is "Triage" or "Pipeline")
        {
            return (0, 0);
        }

        var caseIsClosed = caseStatus is "Closed" or "Complete";
        var stagePastService = caseStage.Length > 0 && StagesPastService.Contains(caseStage);

        var templateCmd = connection.CreateCommand();
        templateCmd.Transaction = tx;
        templateCmd.CommandText = """
            SELECT id, trigger_field, offset_days, title, severity
            FROM deadline_templates
            WHERE active = 1
            """;

        var templates = new List<(long Id, string TriggerField, int OffsetDays, string Title, string Severity)>();
        await using (var reader = await templateCmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                templates.Add((reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3), reader.GetString(4)));
            }
        }

        // Fallback dedup by title: deadline_templates gets wiped and reseeded (fresh
        // autoincrement ids) whenever DeadlineTemplateVersion bumps, which would otherwise
        // orphan the "Computed:{template.Id}" source_type on already-generated rows and
        // let Refresh From Templates add a same-titled duplicate. See the analogous fix/comment
        // in GenerateChecklistForCaseAsync.
        var existingTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingTitlesCmd = connection.CreateCommand();
        existingTitlesCmd.Transaction = tx;
        existingTitlesCmd.CommandText = "SELECT title FROM deadlines WHERE case_id=@caseId";
        existingTitlesCmd.Parameters.AddWithValue("@caseId", caseId);
        await using (var existingTitlesReader = await existingTitlesCmd.ExecuteReaderAsync())
        {
            while (await existingTitlesReader.ReadAsync())
            {
                existingTitles.Add(existingTitlesReader.GetString(0));
            }
        }

        var added = 0;
        var updated = 0;
        var now = DateTime.UtcNow.ToString("O");
        foreach (var template in templates)
        {
            var anchor = template.TriggerField switch
            {
                "filing_date" => filingDate,
                "trial_date" => trialDate,
                "service_perfected_date" => servicePerfectedDate,
                _ => null
            };
            if (anchor is null)
            {
                continue;
            }

            var computedDue = anchor.Value.AddDays(template.OffsetDays).ToString("yyyy-MM-dd");
            var sourceType = $"Computed:{template.Id}";

            var existingCmd = connection.CreateCommand();
            existingCmd.Transaction = tx;
            existingCmd.CommandText = "SELECT id, due_date, status FROM deadlines WHERE case_id=@caseId AND source_type=@sourceType";
            existingCmd.Parameters.AddWithValue("@caseId", caseId);
            existingCmd.Parameters.AddWithValue("@sourceType", sourceType);
            long? existingId = null;
            string? existingDueDate = null;
            string? existingStatus = null;
            await using (var existingReader = await existingCmd.ExecuteReaderAsync())
            {
                if (await existingReader.ReadAsync())
                {
                    existingId = existingReader.GetInt64(0);
                    existingDueDate = existingReader.IsDBNull(1) ? null : existingReader.GetString(1);
                    existingStatus = existingReader.IsDBNull(2) ? null : existingReader.GetString(2);
                }
            }

            if (existingId is null && existingTitles.Contains(template.Title))
            {
                continue;
            }

            if (existingId is null)
            {
                var insert = connection.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = """
                    INSERT INTO deadlines (case_id, title, due_date, status, notes, source_type, is_manual, severity, created_at, updated_at,
                        source_kind, source_template_id, source_template_version, source_stage, generated_at, generated_by)
                    VALUES (@case_id, @title, @due_date, 'Open', NULL, @source_type, 0, @severity, @now, @now,
                        'DeadlineTemplate', @template_id, @source_template_version, @stage, @now, @generated_by);
                    SELECT last_insert_rowid();
                    """;
                insert.Parameters.AddWithValue("@case_id", caseId);
                insert.Parameters.AddWithValue("@title", template.Title);
                insert.Parameters.AddWithValue("@due_date", computedDue);
                insert.Parameters.AddWithValue("@source_type", sourceType);
                insert.Parameters.AddWithValue("@template_id", template.Id.ToString());
                insert.Parameters.AddWithValue("@stage", caseStage);
                insert.Parameters.AddWithValue("@severity", template.Severity);
                insert.Parameters.AddWithValue("@now", now);
                insert.Parameters.AddWithValue("@source_template_version", int.Parse(DeadlineTemplateVersion));
                insert.Parameters.AddWithValue("@generated_by",_actor.AuditLabel);
                existingId = Convert.ToInt64(await insert.ExecuteScalarAsync());
                existingStatus = "Open";
                existingTitles.Add(template.Title);
                added++;
            }
            else
            {
                var lockedCmd = connection.CreateCommand();
                lockedCmd.Transaction = tx;
                lockedCmd.CommandText = "SELECT COUNT(*) FROM deadline_history WHERE deadline_id=@id";
                lockedCmd.Parameters.AddWithValue("@id", existingId);
                var isLocked = Convert.ToInt32(await lockedCmd.ExecuteScalarAsync()) > 0;

                if (!isLocked && existingDueDate != computedDue)
                {
                    var update = connection.CreateCommand();
                    update.Transaction = tx;
                    update.CommandText = "UPDATE deadlines SET due_date=@due_date, title=@title, severity=@severity, updated_at=@now WHERE id=@id";
                    update.Parameters.AddWithValue("@due_date", computedDue);
                    update.Parameters.AddWithValue("@title", template.Title);
                    update.Parameters.AddWithValue("@severity", template.Severity);
                    update.Parameters.AddWithValue("@now", now);
                    update.Parameters.AddWithValue("@id", existingId);
                    await update.ExecuteNonQueryAsync();
                    await LogDeadlineHistoryAsync(connection, tx, existingId.Value, existingDueDate, computedDue, $"Recalculated after {template.TriggerField.Replace('_', ' ')} changed.");
                    updated++;
                }
            }

            // A closed case has nothing left to remind anyone about, regardless of trigger field.
            // A filing-date-anchored service reminder is also moot once service is confirmed
            // perfected, or once the case's own stage shows it has already moved past Service -
            // both are independent proof service happened, even when the boolean flag itself was
            // never set (e.g. historical bulk-imported cases with no source column for it).
            var isMoot = caseIsClosed
                || (template.TriggerField == "filing_date" && (servicePerfected || stagePastService));
            if (isMoot && existingStatus is not ("Done" or "Complete"))
            {
                var why = caseIsClosed
                    ? "Auto-resolved: case is closed."
                    : servicePerfected
                        ? "Auto-resolved: service perfected."
                        : "Auto-resolved: case stage shows service is already complete.";
                var closeCmd = connection.CreateCommand();
                closeCmd.Transaction = tx;
                closeCmd.CommandText = """
                    UPDATE deadlines
                    SET status='Done',
                        notes = CASE WHEN COALESCE(notes, '') = '' THEN @why ELSE notes || ' | ' || @why END,
                        updated_at=@now
                    WHERE id=@id
                    """;
                closeCmd.Parameters.AddWithValue("@why", why);
                closeCmd.Parameters.AddWithValue("@now", now);
                closeCmd.Parameters.AddWithValue("@id", existingId);
                await closeCmd.ExecuteNonQueryAsync();
            }
        }

        return (added, updated);
    }

    public async Task<int> GenerateChecklistAsync(long caseId)
    {
        return await WithWriteAsync(async (connection, tx) =>
        {
            var added = await GenerateChecklistForCaseAsync(connection, tx, caseId);
            await SetAppSettingAsync(connection, tx, "last_save_result", $"Generated {added} checklist item(s) for case {caseId} at {DateTime.Now:G}");
            return added;
        });
    }

    private async Task<int> GenerateChecklistForCaseAsync(SqliteConnection connection, SqliteTransaction tx, long caseId)
    {
        var caseCmd = connection.CreateCommand();
        caseCmd.Transaction = tx;
        caseCmd.CommandText = "SELECT COALESCE(stage,''), COALESCE(track,'Contested'), COALESCE(status,''), COALESCE(case_status,'') FROM cases WHERE id=@id";
        caseCmd.Parameters.AddWithValue("@id", caseId);
        string stage;
        string track;
        string workflowStatus;
        await using (var caseReader = await caseCmd.ExecuteReaderAsync())
        {
            if (!await caseReader.ReadAsync())
            {
                return 0;
            }

            // Triage cases skip template generation - their stage/track aren't confirmed yet
            // (same gate as GenerateDeadlinesForCaseAsync).
            if (caseReader.GetString(2) is "Triage" or "Pipeline" || caseReader.GetString(3) is "Triage" or "Pipeline")
            {
                return 0;
            }

            stage = caseReader.GetString(0);
            track = caseReader.GetString(1);
            workflowStatus = string.IsNullOrWhiteSpace(caseReader.GetString(3))
                ? MapConsolidatedCaseStatus(caseReader.GetString(2), stage, track)
                : caseReader.GetString(3);
        }

        var tags = new List<string>();
        var tagCmd = connection.CreateCommand();
        tagCmd.Transaction = tx;
        tagCmd.CommandText = """
            SELECT it.name FROM case_issue_tags cit
            JOIN issue_tags it ON it.id = cit.issue_tag_id
            WHERE cit.case_id = @caseId
            """;
        tagCmd.Parameters.AddWithValue("@caseId", caseId);
        await using (var tagReader = await tagCmd.ExecuteReaderAsync())
        {
            while (await tagReader.ReadAsync())
            {
                tags.Add(tagReader.GetString(0));
            }
        }

        var existing = new HashSet<string>(StringComparer.Ordinal);
        var existingCmd = connection.CreateCommand();
        existingCmd.Transaction = tx;
        existingCmd.CommandText = "SELECT source_type FROM checklist_items WHERE case_id=@caseId AND source_type LIKE 'Template:%'";
        existingCmd.Parameters.AddWithValue("@caseId", caseId);
        await using (var existingReader = await existingCmd.ExecuteReaderAsync())
        {
            while (await existingReader.ReadAsync())
            {
                existing.Add(existingReader.GetString(0));
            }
        }

        // Also skip candidates that already exist as manual/imported items with the same
        // phase+task text - otherwise a task entered by hand or via import (which has a
        // source_type that never matches 'Template:%') looks like a text duplicate once
        // Refresh From Templates adds the template's copy of the same task alongside it.
        var existingText = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingTextCmd = connection.CreateCommand();
        existingTextCmd.Transaction = tx;
        existingTextCmd.CommandText = "SELECT COALESCE(phase,''), task FROM checklist_items WHERE case_id=@caseId";
        existingTextCmd.Parameters.AddWithValue("@caseId", caseId);
        await using (var existingTextReader = await existingTextCmd.ExecuteReaderAsync())
        {
            while (await existingTextReader.ReadAsync())
            {
                existingText.Add($"{existingTextReader.GetString(0)}{existingTextReader.GetString(1)}");
            }
        }

        var candidateCmd = connection.CreateCommand();
        candidateCmd.Transaction = tx;
        var tagFilter = "0 = 1";
        if (tags.Count > 0)
        {
            var tagParams = new List<string>();
            for (var i = 0; i < tags.Count; i++)
            {
                var paramName = $"@tag{i}";
                tagParams.Add(paramName);
                candidateCmd.Parameters.AddWithValue(paramName, tags[i]);
            }

            tagFilter = $"t.trigger_type = 'IssueTag' AND t.issue_tag_name IN ({string.Join(", ", tagParams)}) AND (t.stage IS NULL OR t.stage = '' OR t.stage = @stage)";
        }

        candidateCmd.CommandText = $"""
            SELECT t.name, ti.sort_order, ti.task, ti.phase, ti.due_offset_days
            FROM checklist_template_items ti
            JOIN checklist_templates t ON t.id = ti.template_id
            WHERE t.active = 1
              AND (
                    (t.trigger_type = 'Stage' AND t.stage = @stage)
                 OR ({tagFilter})
              )
            ORDER BY t.id, ti.sort_order, ti.id
            """;
            candidateCmd.Parameters.AddWithValue("@stage", workflowStatus);

        // Keyed by template name + sort_order (stable across reseeds), not the DB row id -
        // checklist_template_items.id gets wiped and reassigned every time ChecklistTemplateVersion
        // bumps, which previously orphaned every already-generated item's source_type and caused
        // silent duplicate regeneration (see DeduplicateChecklistItemsAsync one-time cleanup).
        var candidates = new List<(string TemplateName, int SortOrder, string Task, string? Phase, int? DueOffsetDays)>();
        await using (var reader = await candidateCmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                candidates.Add((
                    reader.GetString(0),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4)));
            }
        }

        var added = 0;
        var now = DateTime.UtcNow.ToString("O");
        var today = DateOnly.FromDateTime(DateTime.Today);
        foreach (var candidate in candidates)
        {
            var sourceType = $"Template:{candidate.TemplateName}:{candidate.SortOrder}";
            if (existing.Contains(sourceType))
            {
                continue;
            }

            if (existingText.Contains($"{candidate.Phase ?? ""}{candidate.Task}"))
            {
                continue;
            }

            var insert = connection.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO checklist_items (case_id, phase, task, due_date, status, notes, source_type, is_manual, created_at, updated_at,
                    source_kind, source_template_id, source_template_version, source_stage, generated_at, generated_by)
                VALUES (@case_id, @phase, @task, @due_date, 'Not Started', NULL, @source_type, 0, @now, @now,
                    'StageTemplate', @template_id, 1, @stage, @now, @generated_by)
                """;
            insert.Parameters.AddWithValue("@case_id", caseId);
            insert.Parameters.AddWithValue("@phase", DbValue(candidate.Phase));
            insert.Parameters.AddWithValue("@task", candidate.Task);
            insert.Parameters.AddWithValue("@due_date", candidate.DueOffsetDays is { } offset ? today.AddDays(offset).ToString("yyyy-MM-dd") : DBNull.Value);
            insert.Parameters.AddWithValue("@source_type", sourceType);
            insert.Parameters.AddWithValue("@template_id", $"{candidate.TemplateName}:{candidate.SortOrder}");
            insert.Parameters.AddWithValue("@stage", workflowStatus);
            insert.Parameters.AddWithValue("@now", now);
            insert.Parameters.AddWithValue("@generated_by",_actor.AuditLabel);
            await insert.ExecuteNonQueryAsync();
            existing.Add(sourceType);
            existingText.Add($"{candidate.Phase ?? ""}{candidate.Task}");
            added++;
        }

        return added;
    }

    private async Task AddColumnIfMissingAsync(SqliteConnection connection, string table, string column, string definition)
    {
        if (await ColumnExistsAsync(connection, null, table, column))
        {
            return;
        }

        var cmd = connection.CreateCommand();
        cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<T> WithWriteAsync<T>(Func<SqliteConnection, SqliteTransaction, Task<T>> action)
    {
        if (!_paths.IsSafeWritableDatabase(out var message))
        {
            throw new InvalidOperationException($"Writes are disabled for the current database. {message}");
        }

        await _writeGate.WaitAsync();
        try
        {
            var backupPath = await BackupDatabaseAsync();
            await LogAsync($"Backup created: {backupPath}");
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();
            await using var tx = connection.BeginTransaction();
            try
            {
                var result = await action(connection, tx);
                await tx.CommitAsync();
                return result;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
        catch
        {
            throw;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task<string> BackupDatabaseAsync()
    {
        _paths.EnsureFolders();
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        var backupPath = Path.Combine(_paths.Config.BackupsFolder, $"case_planner_web_{stamp}.sqlite");
        var suffix = 0;
        while (File.Exists(backupPath))
        {
            suffix++;
            backupPath = Path.Combine(_paths.Config.BackupsFolder, $"case_planner_web_{stamp}_{suffix:00}.sqlite");
        }
        if (!File.Exists(_paths.Config.DatabasePath))
        {
            await RetryFileOperationAsync(async () => await File.WriteAllTextAsync(backupPath, ""));
            return backupPath;
        }

        await RetryFileOperationAsync(async () =>
        {
            await using var source = new FileStream(_paths.Config.DatabasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, FileOptions.Asynchronous);
            await using var dest = new FileStream(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite, 81920, FileOptions.Asynchronous);
            await source.CopyToAsync(dest);
            await dest.FlushAsync();
        });

        PruneOldBackups();
        return backupPath;
    }

    private static async Task RetryFileOperationAsync(Func<Task> action)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                await Task.Delay(75 * attempt);
            }
        }
    }

    private const int BackupRetentionCount = 20;

    private void PruneOldBackups()
    {
        var backups = Directory.GetFiles(_paths.Config.BackupsFolder, "case_planner_web_*.sqlite")
            .OrderByDescending(path => path, StringComparer.Ordinal)
            .Skip(BackupRetentionCount);
        foreach (var path in backups)
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // Best-effort cleanup; a locked or already-removed file should not fail the write.
            }
        }
    }

    public Task<List<BackupInfo>> GetBackupsAsync()
    {
        _paths.EnsureFolders();
        var backups = Directory.GetFiles(_paths.Config.BackupsFolder, "case_planner_web_*.sqlite")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.Name, StringComparer.Ordinal)
            .Select(file => new BackupInfo
            {
                FileName = file.Name,
                SizeBytes = file.Length,
                CreatedAt = file.LastWriteTimeUtc.ToString("O")
            })
            .ToList();
        return Task.FromResult(backups);
    }

    public async Task<BackupInfo> CreateBackupNowAsync()
    {
        if (!_paths.IsSafeWritableDatabase(out var message))
        {
            throw new InvalidOperationException($"Writes are disabled for the current database. {message}");
        }

        await _writeGate.WaitAsync();
        try
        {
            var backupPath = await BackupDatabaseAsync();
            await LogAsync($"Manual backup created: {backupPath}");
            var file = new FileInfo(backupPath);
            return new BackupInfo { FileName = file.Name, SizeBytes = file.Length, CreatedAt = file.LastWriteTimeUtc.ToString("O") };
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task RestoreBackupAsync(string fileName)
    {
        // fileName must be a bare file name (no directory components) matching a real backup -
        // this is the only guard against path traversal since it flows straight into Path.Combine.
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
        {
            throw new InvalidOperationException("Invalid backup file name.");
        }

        var sourcePath = Path.Combine(_paths.Config.BackupsFolder, fileName);
        if (!File.Exists(sourcePath))
        {
            throw new InvalidOperationException("Backup file not found.");
        }

        if (!_paths.IsSafeWritableDatabase(out var message))
        {
            throw new InvalidOperationException($"Writes are disabled for the current database. {message}");
        }

        await _writeGate.WaitAsync();
        try
        {
            // Safety net: back up whatever is live right now, so a restore can itself be undone.
            var safetyBackupPath = await BackupDatabaseAsync();
            await LogAsync($"Pre-restore safety backup created: {safetyBackupPath}");

            // Microsoft.Data.Sqlite pools native connection handles per connection string by
            // default; disposing a SqliteConnection doesn't guarantee the underlying file handle
            // is released. Clear the pool before AND after the file swap so nothing pooled is
            // holding a stale handle to the old file, or ends up pooled against a half-written one.
            SqliteConnection.ClearAllPools();

            await RetryFileOperationAsync(async () =>
            {
                await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, FileOptions.Asynchronous);
                await using var dest = new FileStream(_paths.Config.DatabasePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 81920, FileOptions.Asynchronous);
                await source.CopyToAsync(dest);
                await dest.FlushAsync();
            });

            SqliteConnection.ClearAllPools();
            await LogAsync($"Database restored from backup: {fileName}");
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task SaveCaseInternalAsync(SqliteConnection connection, SqliteTransaction tx, CaseRecord model)
    {
        if (string.IsNullOrWhiteSpace(model.Status)) model.Status = "Pipeline";
        if (string.IsNullOrWhiteSpace(model.CaseStatus) || model.CaseStatus == "Pipeline" && model.Status != "Pipeline")
        {
            model.CaseStatus = MapConsolidatedCaseStatus(model.Status, model.Stage, model.Track, model.CaseNumber, model.PipelineStage);
        }
        // Auto-fill and persist the 120-day service deadline from the filing date (or an explicit
        // basis date, if set) the first time it's missing, so it's a real stored value rather than
        // something only ever derived live by BuildServiceStatus. A manual value already present
        // always wins.
        if (string.IsNullOrWhiteSpace(model.ServiceDeadline120))
        {
            var basisDate = ParseDate(model.ServiceDeadlineBasisDate) ?? ParseDate(model.FilingDate);
            if (basisDate is not null)
            {
                model.ServiceDeadline120 = basisDate.Value.AddDays(120).ToString("yyyy-MM-dd");
            }
        }

        var now = DateTime.UtcNow.ToString("O");
        var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        if (model.Id == 0)
        {
            cmd.CommandText = """
                INSERT INTO cases (
                    case_number, case_name, job_number, tract, county, status, stage, track, filing_date,
                    date_of_taking, trial_date, next_action, next_action_due, deposit_amount,
                    owner, landowner, valuation_notes, settlement_notes, publication_service_notes,
                    service_required, service_perfected, service_perfected_date, service_deadline_120,
                    service_deadline_basis_date, service_method, service_notes, service_status,
                    assigned_attorney, opposing_counsel, appraiser, taxes_owed,
                    funds_withdrawn, funds_withdrawn_date, discovery_completed, updated_appraisal, closed_date, date_opened,
                    project_name, tax_owed_amount, whole_property_acres, acquisition_acres,
                    landowner_appraiser_name, additional_deposit_amount, additional_deposit_date,
                    matter_type, priority, current_holder, pipeline_stage, date_sent_to_current_holder,
                    next_review_date, momentum_status, waiting_reason, waiting_on, waiting_started_date,
                    expected_response, waiting_follow_up_date, waiting_escalation_action, trial_track,
                    short_posture_summary, current_issue,
                    deferred_until, deferred_reason, deferred_at, deferred_by,
                    case_status, status_mapping_review,
                    trial_end_date, property_description,
                    created_at, updated_at
                ) VALUES (
                    @case_number, @case_name, @job_number, @tract, @county, @status, @stage, @track, @filing_date,
                    @date_of_taking, @trial_date, @next_action, @next_action_due, @deposit_amount,
                    @owner, @landowner, @valuation_notes, @settlement_notes, @publication_service_notes,
                    @service_required, @service_perfected, @service_perfected_date, @service_deadline_120,
                    @service_deadline_basis_date, @service_method, @service_notes, @service_status,
                    @assigned_attorney, @opposing_counsel, @appraiser, @taxes_owed,
                    @funds_withdrawn, @funds_withdrawn_date, @discovery_completed, @updated_appraisal, @closed_date, @date_opened,
                    @project_name, @tax_owed_amount, @whole_property_acres, @acquisition_acres,
                    @landowner_appraiser_name, @additional_deposit_amount, @additional_deposit_date,
                    @matter_type, @priority, @current_holder, @pipeline_stage, @date_sent_to_current_holder,
                    @next_review_date, @momentum_status, @waiting_reason, @waiting_on, @waiting_started_date,
                    @expected_response, @waiting_follow_up_date, @waiting_escalation_action, @trial_track,
                    @short_posture_summary, @current_issue,
                    @deferred_until, @deferred_reason, @deferred_at, @deferred_by,
                    @case_status, @status_mapping_review,
                    @trial_end_date, @property_description,
                    @created_at, @updated_at
                );
                SELECT last_insert_rowid();
                """;
        }
        else
        {
            cmd.CommandText = """
                UPDATE cases SET
                    case_number=@case_number,
                    case_name=@case_name,
                    job_number=@job_number,
                    tract=@tract,
                    county=@county,
                    status=@status,
                    stage=@stage,
                    track=@track,
                    filing_date=@filing_date,
                    date_of_taking=@date_of_taking,
                    trial_date=@trial_date,
                    next_action=@next_action,
                    next_action_due=@next_action_due,
                    deposit_amount=@deposit_amount,
                    owner=@owner,
                    landowner=@landowner,
                    valuation_notes=@valuation_notes,
                    settlement_notes=@settlement_notes,
                    publication_service_notes=@publication_service_notes,
                    service_required=@service_required,
                    service_perfected=@service_perfected,
                    service_perfected_date=@service_perfected_date,
                    service_deadline_120=@service_deadline_120,
                    service_deadline_basis_date=@service_deadline_basis_date,
                    service_method=@service_method,
                    service_notes=@service_notes,
                    service_status=@service_status,
                    assigned_attorney=@assigned_attorney,
                    opposing_counsel=@opposing_counsel,
                    appraiser=@appraiser,
                    taxes_owed=@taxes_owed,
                    funds_withdrawn=@funds_withdrawn,
                    funds_withdrawn_date=@funds_withdrawn_date,
                    discovery_completed=@discovery_completed,
                    updated_appraisal=@updated_appraisal,
                    closed_date=@closed_date,
                    date_opened=@date_opened,
                    project_name=@project_name,
                    tax_owed_amount=@tax_owed_amount,
                    whole_property_acres=@whole_property_acres,
                    acquisition_acres=@acquisition_acres,
                    landowner_appraiser_name=@landowner_appraiser_name,
                    additional_deposit_amount=@additional_deposit_amount,
                    additional_deposit_date=@additional_deposit_date,
                    matter_type=@matter_type,
                    priority=@priority,
                    current_holder=@current_holder,
                    pipeline_stage=@pipeline_stage,
                    date_sent_to_current_holder=@date_sent_to_current_holder,
                    next_review_date=@next_review_date,
                    momentum_status=@momentum_status,
                    waiting_reason=@waiting_reason,
                    waiting_on=@waiting_on,
                    waiting_started_date=@waiting_started_date,
                    expected_response=@expected_response,
                    waiting_follow_up_date=@waiting_follow_up_date,
                    waiting_escalation_action=@waiting_escalation_action,
                    trial_track=@trial_track,
                    short_posture_summary=@short_posture_summary,
                    current_issue=@current_issue,
                    deferred_until=@deferred_until,
                    deferred_reason=@deferred_reason,
                    deferred_at=@deferred_at,
                    deferred_by=@deferred_by,
                    case_status=@case_status,
                    status_mapping_review=@status_mapping_review,
                    trial_end_date=@trial_end_date,
                    property_description=@property_description,
                    updated_at=@updated_at
                WHERE id=@id;
                SELECT @id;
                """;
            cmd.Parameters.AddWithValue("@id", model.Id);
        }

        AddCaseParameters(cmd, model, now);
        model.Id = Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static string MapConsolidatedCaseStatus(string? status, string? stage, string? track, string? caseNumber = null, string? pipelineStage = null)
    {
        if (status == "Triage") return "Triage";
        if (status is "Closed" or "Complete" || stage == "Resolved") return "Resolved / Closed";
        if (status == "Pipeline" || (string.IsNullOrWhiteSpace(caseNumber) && !string.IsNullOrWhiteSpace(pipelineStage))) return "Pipeline";
        if (track == "Settlement") return "Settlement Pending";
        if (stage == "Trial Track") return "Trial Preparation";
        if (stage == "Service") return "Filed / Service Pending";
        return "Active Litigation";
    }

    private static void AddCaseParameters(SqliteCommand cmd, CaseRecord model, string now)
    {
        cmd.Parameters.AddWithValue("@case_number", model.CaseNumber);
        cmd.Parameters.AddWithValue("@case_name", model.CaseName);
        cmd.Parameters.AddWithValue("@job_number", DbValue(model.JobNumber));
        cmd.Parameters.AddWithValue("@tract", DbValue(model.Tract));
        cmd.Parameters.AddWithValue("@county", DbValue(model.County));
        cmd.Parameters.AddWithValue("@status", DbValue(model.Status));
        cmd.Parameters.AddWithValue("@stage", DbValue(model.Stage));
        cmd.Parameters.AddWithValue("@track", string.IsNullOrWhiteSpace(model.Track) ? "Contested" : model.Track.Trim());
        cmd.Parameters.AddWithValue("@filing_date", DbValue(model.FilingDate));
        cmd.Parameters.AddWithValue("@date_of_taking", DbValue(model.DateOfTaking));
        cmd.Parameters.AddWithValue("@trial_date", DbValue(model.TrialDate));
        cmd.Parameters.AddWithValue("@next_action", DbValue(model.NextAction));
        cmd.Parameters.AddWithValue("@next_action_due", DbValue(model.NextActionDue));
        cmd.Parameters.AddWithValue("@deposit_amount", model.DepositAmount.HasValue ? model.DepositAmount.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@owner", DbValue(model.Owner));
        cmd.Parameters.AddWithValue("@landowner", DbValue(model.Landowner));
        cmd.Parameters.AddWithValue("@valuation_notes", DbValue(model.ValuationNotes));
        cmd.Parameters.AddWithValue("@settlement_notes", DbValue(model.SettlementNotes));
        cmd.Parameters.AddWithValue("@publication_service_notes", DbValue(model.PublicationServiceNotes));
        cmd.Parameters.AddWithValue("@service_required", model.ServiceRequired ? 1 : 0);
        cmd.Parameters.AddWithValue("@service_perfected", model.ServicePerfected ? 1 : 0);
        cmd.Parameters.AddWithValue("@service_perfected_date", DbValue(model.ServicePerfectedDate));
        cmd.Parameters.AddWithValue("@service_deadline_120", DbValue(model.ServiceDeadline120));
        cmd.Parameters.AddWithValue("@service_deadline_basis_date", DbValue(model.ServiceDeadlineBasisDate));
        cmd.Parameters.AddWithValue("@service_method", DbValue(model.ServiceMethod));
        cmd.Parameters.AddWithValue("@service_notes", DbValue(model.ServiceNotes));
        cmd.Parameters.AddWithValue("@service_status", DbValue(model.ServiceStatus));
        cmd.Parameters.AddWithValue("@assigned_attorney", DbValue(model.AssignedAttorney));
        cmd.Parameters.AddWithValue("@opposing_counsel", DbValue(model.OpposingCounsel));
        cmd.Parameters.AddWithValue("@appraiser", DbValue(model.Appraiser));
        cmd.Parameters.AddWithValue("@taxes_owed", DbValue(model.TaxesOwed));
        cmd.Parameters.AddWithValue("@funds_withdrawn", DbValue(model.FundsWithdrawn));
        cmd.Parameters.AddWithValue("@funds_withdrawn_date", DbValue(model.FundsWithdrawnDate));
        cmd.Parameters.AddWithValue("@discovery_completed", DbValue(model.DiscoveryCompleted));
        cmd.Parameters.AddWithValue("@updated_appraisal", DbValue(model.UpdatedAppraisal));
        cmd.Parameters.AddWithValue("@closed_date", DbValue(model.ClosedDate));
        cmd.Parameters.AddWithValue("@date_opened", DbValue(model.DateOpened));
        cmd.Parameters.AddWithValue("@project_name", DbValue(model.ProjectName));
        cmd.Parameters.AddWithValue("@tax_owed_amount", model.TaxOwedAmount.HasValue ? model.TaxOwedAmount.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@whole_property_acres", model.WholePropertyAcres.HasValue ? model.WholePropertyAcres.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@acquisition_acres", model.AcquisitionAcres.HasValue ? model.AcquisitionAcres.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@landowner_appraiser_name", DbValue(model.LandownerAppraiserName));
        cmd.Parameters.AddWithValue("@additional_deposit_amount", model.AdditionalDepositAmount.HasValue ? model.AdditionalDepositAmount.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@additional_deposit_date", DbValue(model.AdditionalDepositDate));
        cmd.Parameters.AddWithValue("@matter_type", string.IsNullOrWhiteSpace(model.MatterType) ? "FiledCase" : model.MatterType.Trim());
        cmd.Parameters.AddWithValue("@priority", string.IsNullOrWhiteSpace(model.Priority) ? "Normal" : model.Priority.Trim());
        cmd.Parameters.AddWithValue("@current_holder", DbValue(model.CurrentHolder));
        cmd.Parameters.AddWithValue("@pipeline_stage", DbValue(model.PipelineStage));
        cmd.Parameters.AddWithValue("@date_sent_to_current_holder", DbValue(model.DateSentToCurrentHolder));
        cmd.Parameters.AddWithValue("@next_review_date", DbValue(model.NextReviewDate));
        cmd.Parameters.AddWithValue("@momentum_status", DbValue(model.MomentumStatus));
        cmd.Parameters.AddWithValue("@waiting_reason", DbValue(model.WaitingReason));
        cmd.Parameters.AddWithValue("@waiting_on", DbValue(model.WaitingOn));
        cmd.Parameters.AddWithValue("@waiting_started_date", DbValue(model.WaitingStartedDate));
        cmd.Parameters.AddWithValue("@expected_response", DbValue(model.ExpectedResponse));
        cmd.Parameters.AddWithValue("@waiting_follow_up_date", DbValue(model.WaitingFollowUpDate));
        cmd.Parameters.AddWithValue("@waiting_escalation_action", DbValue(model.WaitingEscalationAction));
        cmd.Parameters.AddWithValue("@trial_track", model.TrialTrack ? 1 : 0);
        cmd.Parameters.AddWithValue("@short_posture_summary", DbValue(model.ShortPostureSummary));
        cmd.Parameters.AddWithValue("@current_issue", DbValue(model.CurrentIssue));
        cmd.Parameters.AddWithValue("@deferred_until", DbValue(model.DeferredUntil));
        cmd.Parameters.AddWithValue("@deferred_reason", DbValue(model.DeferredReason));
        cmd.Parameters.AddWithValue("@deferred_at", DbValue(model.DeferredAt));
        cmd.Parameters.AddWithValue("@deferred_by", DbValue(model.DeferredBy));
        cmd.Parameters.AddWithValue("@case_status", string.IsNullOrWhiteSpace(model.CaseStatus) ? "Pipeline" : model.CaseStatus.Trim());
        cmd.Parameters.AddWithValue("@status_mapping_review", model.StatusMappingReview ? 1 : 0);
        cmd.Parameters.AddWithValue("@trial_end_date", DbValue(model.TrialEndDate));
        cmd.Parameters.AddWithValue("@property_description", DbValue(model.PropertyDescription));
        cmd.Parameters.AddWithValue("@created_at", now);
        cmd.Parameters.AddWithValue("@updated_at", now);
    }

    private async Task EnsureImportSampleAsync()
    {
        var samplePath = Path.Combine(_paths.Config.ImportSamplesFolder, "sample_cases.csv");
        var templatePath = Path.Combine(_paths.Config.ImportSamplesFolder, "case_import_template.csv");
        if (!File.Exists(samplePath))
        {
            var lines = new[]
            {
                "Case Number,Case Name,Job Number,Tract,County,Status,Date Opened,Filing Date,Date of Taking,Trial Date,Next Action,Next Action Due,Deposit Amount,Owner,Landowner,Notes",
                "DEMO-CSV-0004,Imported Demo CSV Case,DEMO-JOB-4,DEMO-TRACT-4,Demo County,Active,2026-07-01,2026-07-01,,2026-10-01,Review imported case,2026-07-15,\"$1,250.00\",Demo Owner,Demo Landowner,CSV import sample"
            };
            await File.WriteAllLinesAsync(samplePath, lines);
        }

        if (!File.Exists(templatePath))
        {
            var templateLines = new[]
            {
                "Case Number,Case Name,Job Number,Tract,County,Status,Date Opened,Filing Date,Date of Taking,Trial Date,Next Action,Next Action Due,Deposit Amount,Owner,Landowner,Notes,Service Required,Service Perfected,Service Perfected Date,Service Deadline Basis Date,Service Deadline 120,Service Method,Service Status,Service Notes",
                "DEMO-IMPORT-0001,Demo Import Service Case,DEMO-IMPORT-JOB-1,DEMO-TRACT-A,Demo County,Discovery,2026-06-01,,2026-10-15,Confirm service deadline,2026-07-18,\"$1,500.00\",Demo Owner A,Demo Landowner A,Demo import row,Yes,No,,2026-06-01,,Certified mail + publication,Awaiting perfected service,Blank dates are allowed",
                "DEMO-IMPORT-0002,Demo Import Perfected Case,DEMO-IMPORT-JOB-2,DEMO-TRACT-B,Demo County,Pre-Trial,2026-03-15,,2026-09-10,Review trial outline,2026-07-22,2500,Demo Owner B,Demo Landowner B,Second demo row,Yes,Yes,2026-04-20,2026-03-15,2026-07-13,Personal service,Service Perfected,Service completed before deadline",
                "DEMO-IMPORT-0003,Demo Import Blank-Date Case,DEMO-IMPORT-JOB-3,DEMO-TRACT-C,Demo County,Active,,,,Prepare status note,,\"$3,250.00\",Demo Owner C,Demo Landowner C,1900-01-01 is treated as blank,Yes,No,,,,,Needs deadline set,"
            };
            await File.WriteAllLinesAsync(templatePath, templateLines);
        }
    }

    private async Task SeedAsync(SqliteConnection connection, bool isFreshDatabase)
    {
        if (!isFreshDatabase || await ScalarCountAsync(connection, "cases") > 0)
        {
            return;
        }

        var now = DateTime.UtcNow.ToString("O");
        await ExecuteAsync(connection, """
            INSERT INTO cases (
                case_number, case_name, job_number, tract, county, status, stage, filing_date, trial_date,
                next_action, next_action_due, deposit_amount, owner, landowner, valuation_notes,
                settlement_notes, publication_service_notes, service_required, service_perfected,
                service_perfected_date, service_deadline_120, service_deadline_basis_date,
                service_method, service_notes, service_status, created_at, updated_at
            )
            VALUES
            ('SAMPLE-CASE-001', 'Fictional Sample Pipeline Case', 'SAMPLE-JOB-001', 'SAMPLE-TRACT-001', 'Sample County', 'Pipeline', 'Pipeline', NULL, NULL, 'Review sample intake', NULL, NULL, 'Sample Legal Assistant', 'Fictional Landowner', 'Clearly fictional seed record for testing only.', NULL, NULL, 0, 0, NULL, NULL, NULL, NULL, NULL, 'Not Perfected', '%NOW%', '%NOW%'),
            ('SAMPLE-CASE-002', 'Fictional Sample Service Case', 'SAMPLE-JOB-002', 'SAMPLE-TRACT-002', 'Demo County', 'Filed / Service Pending', 'Filed / Service Pending', '2026-06-01', NULL, 'Complete service follow-up', '2026-07-20', 1250.00, 'Sample Attorney', 'Fictional Owner Two', 'Sample service facts.', NULL, 'Sample publication note.', 1, 0, NULL, '2026-09-29', '2026-06-01', 'Certified mail', 'Awaiting service.', 'Not Perfected', '%NOW%', '%NOW%'),
            ('SAMPLE-CASE-003', 'Fictional Sample Litigation Case', 'SAMPLE-JOB-003', 'SAMPLE-TRACT-003', 'Example County', 'Active Litigation', 'Active Litigation', '2026-03-15', NULL, 'Review discovery responses', '2026-07-25', 2500.00, 'Sample Attorney', 'Fictional Owner Three', 'Sample litigation posture.', NULL, NULL, 1, 1, NULL, '2026-07-13', '2026-03-15', 'Personal service', 'Service marked perfected.', 'Perfected', '%NOW%', '%NOW%'),
            ('SAMPLE-CASE-004', 'Fictional Sample Trial Case', 'SAMPLE-JOB-004', 'SAMPLE-TRACT-004', 'Example County', 'Trial Preparation', 'Trial Preparation', '2025-11-01', '2026-09-15', 'Prepare trial notebook', '2026-08-15', 4000.00, 'Sample Chief Counsel', 'Fictional Owner Four', 'Sample trial preparation facts.', NULL, NULL, 1, 1, NULL, '2026-02-28', '2025-11-01', 'Personal service', 'Service complete.', 'Perfected', '%NOW%', '%NOW%');
            """.Replace("%NOW%", now));
        await ExecuteAsync(connection, """
            INSERT INTO deadlines (case_id, title, due_date, status, notes, source_type, is_manual, created_at, updated_at)
            VALUES ((SELECT id FROM cases WHERE case_number = 'SAMPLE-CASE-001'), 'Review sample intake', NULL, 'Open', 'Fictional sample data only.', 'Manual', 1, '%NOW%', '%NOW%'),
                   ((SELECT id FROM cases WHERE case_number = 'SAMPLE-CASE-002'), 'Complete service follow-up', '2026-07-20', 'Open', 'Fictional sample data only.', 'Manual', 1, '%NOW%', '%NOW%'),
                   ((SELECT id FROM cases WHERE case_number = 'SAMPLE-CASE-004'), 'Prepare trial notebook', '2026-08-15', 'Open', 'Fictional sample data only.', 'Manual', 1, '%NOW%', '%NOW%');
            """.Replace("%NOW%", now));
        await ExecuteAsync(connection, """
            INSERT INTO checklist_items (case_id, phase, task, due_date, status, notes, source_type, is_manual, created_at, updated_at)
            VALUES ((SELECT id FROM cases WHERE case_number = 'SAMPLE-CASE-001'), 'Pipeline', 'Review sample intake', NULL, 'Not Started', 'Fictional sample data only.', 'Manual', 1, '%NOW%', '%NOW%'),
                   ((SELECT id FROM cases WHERE case_number = 'SAMPLE-CASE-003'), 'Active Litigation', 'Review discovery responses', '2026-07-25', 'In Progress', 'Fictional sample data only.', 'Manual', 1, '%NOW%', '%NOW%'),
                   ((SELECT id FROM cases WHERE case_number = 'SAMPLE-CASE-004'), 'Trial Preparation', 'Prepare trial notebook', '2026-08-15', 'Not Started', 'Fictional sample data only.', 'Manual', 1, '%NOW%', '%NOW%');
            """.Replace("%NOW%", now));
        await ExecuteAsync(connection, """
            INSERT INTO discovery_tracking (case_id, direction, discovery_type, served_date, due_date, follow_up_date, status, assigned_to, notes, created_at, updated_at)
            VALUES ((SELECT id FROM cases WHERE case_number = 'SAMPLE-CASE-003'), 'Served on Us', 'Sample discovery item', '2026-06-15', '2026-07-25', '2026-07-30', 'Waiting for Responses', 'Sample Attorney', 'Fictional sample data only.', '%NOW%', '%NOW%');
            """.Replace("%NOW%", now));
        await ExecuteAsync(connection, """
            INSERT INTO publication_dates (case_id, publication_number, publication_date, newspaper, proof_filed, proof_filed_date, response_deadline, service_resolved, notes, created_at, updated_at)
            VALUES ((SELECT id FROM cases WHERE case_number = 'SAMPLE-CASE-002'), '1', '2026-06-20', 'Sample Gazette', 0, NULL, '2026-07-20', 0, 'Fictional sample data only.', '%NOW%', '%NOW%');
            """.Replace("%NOW%", now));
        await ExecuteAsync(connection, """
            INSERT INTO case_issue_tags (case_id, issue_tag_id, notes, created_at, updated_at)
            SELECT (SELECT id FROM cases WHERE case_number = 'SAMPLE-CASE-002'), id, 'Publication monitoring required.', '%NOW%', '%NOW%'
            FROM issue_tags WHERE name = 'Publication Service';
            """.Replace("%NOW%", now));
        await ExecuteAsync(connection, """
            INSERT INTO app_settings (key, value, updated_at)
            VALUES ('sample_data_key', 'sample-case-set-v2', '%NOW%')
            ON CONFLICT(key) DO UPDATE SET value=excluded.value, updated_at=excluded.updated_at;
            """.Replace("%NOW%", now));
    }

    private async Task<int> ScalarCountAsync(SqliteConnection connection, string table)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<bool> ColumnExistsAsync(SqliteConnection connection, SqliteTransaction? tx, string table, string column)
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"PRAGMA table_info({table})";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // Returns the matching case's id and current status - importers need the status so a
    // re-import can preserve an in-progress Triage state instead of prematurely activating it.
    private async Task<(long Id, string Status)?> FindMatchingCaseAsync(SqliteConnection connection, SqliteTransaction tx, string caseNumber, string jobNumber, string tract)
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT id, COALESCE(status, '')
            FROM cases
            WHERE (@case_number <> '' AND case_number = @case_number)
               OR (@job_number <> '' AND @tract <> '' AND job_number = @job_number AND tract = @tract)
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@case_number", caseNumber);
        cmd.Parameters.AddWithValue("@job_number", jobNumber);
        cmd.Parameters.AddWithValue("@tract", tract);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return (reader.GetInt64(0), reader.GetString(1));
        }

        return null;
    }

    private async Task<string?> GetAppSettingAsync(SqliteConnection connection, string key)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM app_settings WHERE key = @key LIMIT 1";
        cmd.Parameters.AddWithValue("@key", key);
        return await cmd.ExecuteScalarAsync() as string;
    }

    private static async Task SetAppSettingAsync(SqliteConnection connection, SqliteTransaction tx, string key, string value)
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO app_settings (key, value, updated_at)
            VALUES (@key, @value, @updated_at)
            ON CONFLICT(key) DO UPDATE SET value=excluded.value, updated_at=excluded.updated_at;
            """;
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        cmd.Parameters.AddWithValue("@updated_at", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static CaseRecord ReadCase(SqliteDataReader reader) => CaseRecordDataMapper.Read(reader);

    // Ports the office's real settlement-posture narrative template (attorney-provided) into
    // a 4-paragraph draft. Tokens either come from data already on file (case record, valuation
    // positions, the risk ledger's AshcFirstOffer/LandownerCounteroffer rows) or from the
    // RiskNarrativeManualInputs the caller collected via a one-time prompt - see
    // GenerateRiskNarrativeAsync. Returns a draft for the attorney to edit, not a final document.
    private static string BuildRiskNarrativeText(
        CaseRecord caseRecord,
        ValuationPositionRecord? ashcPosition,
        ValuationPositionRecord? landownerPosition,
        RiskAnalysisResult risk,
        RiskNarrativeManualInputs manual)
    {
        static string Money(decimal? value) => value.HasValue ? value.Value.ToString("C", CultureInfo.CurrentCulture) : "Not set";
        static string PerSf(decimal? value) => value.HasValue ? value.Value.ToString("C", CultureInfo.CurrentCulture) + "/sf" : "Not set/sf";
        static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "Not set" : value;

        // Rows are now a user-extensible list rather than 5 fixed keys, so "the ASHC offer" and
        // "the landowner counteroffer" are found by offer-maker + position instead of a magic
        // RowKey - first ASHC-maker row ("our opening position") and last Landowner-maker row
        // ("their most recent position"). For a migrated legacy case this resolves to exactly the
        // same two rows the old fixed-key lookup found, since list order is preserved by the
        // migration (AshcFirstOffer before AshcCounteroffer; LandownerCounteroffer comes last).
        var ashcOffer = risk.Rows.FirstOrDefault(r => !r.IsSplit && r.OfferMaker == "ASHC");
        var counteroffer = risk.Rows.LastOrDefault(r => !r.IsSplit && r.OfferMaker == "Landowner");
        var defendantAboveDeposit = landownerPosition?.AppraisedValue is { } defendantTotal
            ? defendantTotal - (caseRecord.DepositAmount ?? 0m)
            : (decimal?)null;
        var overThreshold = ashcOffer?.HourlyRiskStatus == "Computed";

        var tceSentence = string.IsNullOrWhiteSpace(manual.TceDescription) ? "" : $" {manual.TceDescription}";

        var paragraph1 =
            $"This condemnation was filed on {DisplayDate(caseRecord.FilingDate)}, for the purposes of constructing and maintaining highway facilities on {Text(caseRecord.ProjectName)}, " +
            $"in connection with Job No. {Text(caseRecord.JobNumber)}, which involved the acquisition of Tract {Text(caseRecord.Tract)}. " +
            $"The whole existing property was {caseRecord.WholePropertyAcres?.ToString() ?? "Not set"} acres, more or less. The property is {Text(manual.PropertyDescription)}. " +
            $"A total of {caseRecord.AcquisitionAcres?.ToString() ?? "Not set"} acres, more or less, was acquired by the ASHC.{tceSentence}";

        var paragraph2 =
            $"ARDOT prepared a before-and-after appraisal that valued the total acquisition at {Money(ashcPosition?.AppraisedValue)}. " +
            $"The land was valued at {Money(manual.OurAppraisalLandBefore)} ({PerSf(manual.OurAppraisalPerSfBefore)}) before the acquisition and {Money(manual.OurAppraisalLandAfter)} ({PerSf(manual.OurAppraisalPerSfAfter)}) after the acquisition, based on comparable sales. " +
            $"Defendants obtained an appraisal for the amount of {Money(landownerPosition?.AppraisedValue)}, an additional {Money(defendantAboveDeposit)} above the initial deposit. " +
            "Defendants' appraisal also valued the land much higher than the value given by the ARDOT appraisal. " +
            $"The property was valued at {Money(manual.DefendantAppraisalLandBefore)} ({PerSf(manual.DefendantAppraisalPerSfBefore)}) before the acquisition and {Money(manual.DefendantAppraisalLandAfter)} ({PerSf(manual.DefendantAppraisalPerSfAfter)}) after the acquisition, based on comparable sales. " +
            $"Both appraisals found that the property had a highest and best use of {Text(manual.HighestAndBestUse)}.";

        var thresholdSentence = overThreshold
            ? $"As the total amount of {Money(ashcOffer?.JustCompensation)} equals at least 20% above the initial deposit, it would trigger the automatic statutory award of attorney's fees under Ark. Code Ann. § 27-67-317(b) if such an award were made at trial."
            : $"As the total amount of {Money(ashcOffer?.JustCompensation)} does not equal at least 20% above the initial deposit, it would not trigger the automatic statutory award of attorney's fees if such an award were made at trial.";
        var paragraph3 =
            $"Based on the valuation proffered by Defendants' appraisal, a proposal by ASHC of {Money(ashcOffer?.JustCompensation)} was made on {Text(manual.AshcOfferDate)}. " +
            $"An adjustment of {Money(manual.FeeAdjustmentAmount)} was made on the initial offer to account for expenses and attorney's fees. " +
            thresholdSentence;

        var paragraph4 =
            $"On {Text(manual.CounterofferDate)}, Defendants made a counteroffer in the amount of {Money(counteroffer?.JustCompensation)}. " +
            $"Settlement for the sum of {Money(manual.SettlementAmount)} is reasonable. " +
            $"Potential risk of taking the matter to trial could result in total fees of {Money(manual.TrialFeeLow)}-{Money(manual.TrialFeeHigh)} on a {Money(manual.SettlementAmount)} judgment.";

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            paragraph1, paragraph2, "[Add any other necessary specifics about the property and/or negotiations.]", paragraph3, paragraph4);
    }

    private static string BuildDocumentText(CaseWorkspaceResponse workspace, string title)
    {
        var service = workspace.ServiceStatus;
        var serviceDeadlineText = service.ServiceDeadline120 is null
            ? "Not set"
            : service.DaysRemaining is < 0
                ? $"{service.ServiceDeadline120} ({Math.Abs(service.DaysRemaining.Value)} days overdue)"
                : service.DaysRemaining is { } daysRemaining
                    ? $"{service.ServiceDeadline120} ({daysRemaining} days remaining)"
                    : service.ServiceDeadline120;
        return
            $"{title}{Environment.NewLine}" +
            $"Case: {workspace.Case.CaseName} ({workspace.Case.CaseNumber}){Environment.NewLine}" +
            $"Job / Tract: {workspace.Case.JobNumber} / {workspace.Case.Tract}{Environment.NewLine}" +
            $"County / Status: {workspace.Case.County} / {workspace.Case.Status}{Environment.NewLine}" +
            $"Trial Date: {DisplayDate(workspace.Case.TrialDate)}{Environment.NewLine}" +
            $"Next Action: {workspace.Case.NextAction ?? "Not set"}{Environment.NewLine}" +
            $"Next Action Due: {DisplayDate(workspace.Case.NextActionDue)}{Environment.NewLine}" +
            $"Deposit Amount: {(workspace.Case.DepositAmount.HasValue ? workspace.Case.DepositAmount.Value.ToString("C", CultureInfo.CurrentCulture) : "Not set")}{Environment.NewLine}" +
            $"{Environment.NewLine}Service Status{Environment.NewLine}" +
            $"- Service perfected: {(service.ServicePerfected ? "Yes" : "No")}{Environment.NewLine}" +
            $"- Service perfected date: {DisplayDate(service.ServicePerfectedDate)}{Environment.NewLine}" +
            $"- 120-day service deadline: {serviceDeadlineText}{Environment.NewLine}" +
            $"- Service method / status: {service.ServiceMethod ?? "Not set"} / {service.ServiceStatus ?? "Not set"}{Environment.NewLine}" +
            $"- Service notes: {service.ServiceNotes ?? "No service notes"}{Environment.NewLine}" +
            $"{Environment.NewLine}Deadlines{Environment.NewLine}" +
            string.Join(Environment.NewLine, workspace.Deadlines.Select(d => $"- {d.Title} [{d.Status}] due {DisplayDate(d.DueDate)}")) +
            $"{Environment.NewLine}{Environment.NewLine}Checklist{Environment.NewLine}" +
            string.Join(Environment.NewLine, workspace.ChecklistItems.Select(i => $"- {i.Phase}: {i.Task} [{i.Status}] due {DisplayDate(i.DueDate)}")) +
            $"{Environment.NewLine}{Environment.NewLine}Discovery{Environment.NewLine}" +
            string.Join(Environment.NewLine, workspace.DiscoveryItems.Select(i => $"- {i.Direction} {i.DiscoveryType} [{i.Status}] due {DisplayDate(i.DueDate)}")) +
            $"{Environment.NewLine}{Environment.NewLine}Service / Publication Details{Environment.NewLine}" +
            string.Join(Environment.NewLine, workspace.PublicationEntries.Select(p => $"- Publication #{p.PublicationNumber}: {DisplayDate(p.PublicationDate)} | {p.Newspaper ?? "No newspaper"} | proof filed: {(p.ProofFiled ? "Yes" : "No")}")) +
            $"{Environment.NewLine}{Environment.NewLine}Issue Tags{Environment.NewLine}" +
            string.Join(Environment.NewLine, workspace.CaseIssueTags.Select(t => $"- {t.TagName}")) +
            $"{Environment.NewLine}{Environment.NewLine}Recommended Next Actions{Environment.NewLine}" +
            "- Review deadlines and checklist items due soon." + Environment.NewLine +
            "- Update discovery follow-up status as needed." + Environment.NewLine +
            "- Confirm service is perfected before the 120-day service deadline.";
    }

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS cases (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            case_number TEXT NOT NULL,
            case_name TEXT NOT NULL,
            job_number TEXT,
            tract TEXT,
            county TEXT,
            status TEXT,
            stage TEXT,
            case_type TEXT DEFAULT 'Standard',
            filing_date TEXT,
            date_of_taking TEXT,
            trial_date TEXT,
            next_action TEXT,
            next_action_due TEXT,
            deposit_amount REAL,
            owner TEXT,
            landowner TEXT,
            valuation_notes TEXT,
            settlement_notes TEXT,
            publication_service_notes TEXT,
            service_required INTEGER DEFAULT 1,
            service_perfected INTEGER DEFAULT 0,
            service_perfected_date TEXT,
            service_deadline_120 TEXT,
            service_deadline_basis_date TEXT,
            service_method TEXT,
            service_notes TEXT,
            service_status TEXT,
            assigned_attorney TEXT,
            opposing_counsel TEXT,
            appraiser TEXT,
            taxes_owed TEXT,
            funds_withdrawn TEXT,
            discovery_completed TEXT,
            updated_appraisal TEXT,
            closed_date TEXT,
            date_opened TEXT,
            created_at TEXT,
            updated_at TEXT
        );
        CREATE TABLE IF NOT EXISTS deadlines (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            case_id INTEGER NOT NULL,
            title TEXT NOT NULL,
            due_date TEXT,
            status TEXT,
            notes TEXT,
            source_type TEXT,
            is_manual INTEGER DEFAULT 1,
            severity TEXT DEFAULT 'normal',
            created_at TEXT,
            updated_at TEXT
        );
        CREATE TABLE IF NOT EXISTS deadline_templates (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL,
            trigger_field TEXT NOT NULL,
            offset_days INTEGER NOT NULL,
            title TEXT NOT NULL,
            severity TEXT NOT NULL DEFAULT 'normal',
            case_type TEXT NOT NULL DEFAULT 'Any',
            track TEXT NOT NULL DEFAULT 'Any',
            active INTEGER DEFAULT 1,
            created_at TEXT,
            updated_at TEXT
        );
        CREATE TABLE IF NOT EXISTS deadline_history (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            deadline_id INTEGER NOT NULL,
            previous_due_date TEXT,
            new_due_date TEXT,
            reason TEXT,
            created_at TEXT
        );
        CREATE TABLE IF NOT EXISTS checklist_items (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            case_id INTEGER NOT NULL,
            phase TEXT,
            task TEXT NOT NULL,
            due_date TEXT,
            status TEXT,
            notes TEXT,
            source_type TEXT,
            is_manual INTEGER DEFAULT 1,
            created_at TEXT,
            updated_at TEXT
        );
        CREATE TABLE IF NOT EXISTS discovery_tracking (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            case_id INTEGER NOT NULL,
            request_title TEXT,
            direction TEXT,
            discovery_type TEXT,
            served_date TEXT,
            due_date TEXT,
            response_date TEXT,
            follow_up_date TEXT,
            status TEXT,
            assigned_to TEXT,
            notes TEXT,
            escalation_note TEXT,
            good_faith_sent_date TEXT,
            motion_to_compel_date TEXT,
            created_at TEXT,
            updated_at TEXT
        );
        CREATE TABLE IF NOT EXISTS publication_dates (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            case_id INTEGER NOT NULL,
            publication_number TEXT,
            publication_date TEXT,
            newspaper TEXT,
            proof_filed INTEGER DEFAULT 0,
            proof_filed_date TEXT,
            response_deadline TEXT,
            service_resolved INTEGER DEFAULT 0,
            notes TEXT,
            created_at TEXT,
            updated_at TEXT
        );
        CREATE TABLE IF NOT EXISTS case_publications (
            case_id INTEGER PRIMARY KEY,
            first_publication_date TEXT,
            second_publication_date TEXT,
            publication_name TEXT,
            marked_perfected INTEGER DEFAULT 0,
            last_updated_at TEXT,
            last_updated_by TEXT
        );
        CREATE TABLE IF NOT EXISTS discovery_template_items (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            stable_key TEXT NOT NULL,
            version INTEGER NOT NULL,
            category TEXT NOT NULL,
            issue_tag_name TEXT,
            track TEXT NOT NULL DEFAULT 'Any',
            wording TEXT NOT NULL,
            enabled INTEGER NOT NULL DEFAULT 1,
            sort_order INTEGER NOT NULL DEFAULT 0,
            is_default INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL,
            created_by TEXT,
            UNIQUE(stable_key, version)
        );
        CREATE TABLE IF NOT EXISTS valuation_positions (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            case_id INTEGER NOT NULL,
            side TEXT NOT NULL,
            appraiser_name TEXT,
            appraised_value REAL,
            value_date TEXT,
            methodology TEXT,
            notes TEXT,
            created_at TEXT,
            updated_at TEXT,
            UNIQUE(case_id, side)
        );
        CREATE TABLE IF NOT EXISTS comparable_sales (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            case_id INTEGER NOT NULL,
            side TEXT NOT NULL,
            sale_description TEXT,
            sale_price REAL,
            sale_date TEXT,
            size_acres REAL,
            adjustment_notes TEXT,
            notes TEXT,
            created_at TEXT,
            updated_at TEXT
        );
        CREATE TABLE IF NOT EXISTS case_opposing_attorneys (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            case_id INTEGER NOT NULL,
            name TEXT NOT NULL,
            sort_order INTEGER NOT NULL DEFAULT 0,
            created_at TEXT,
            updated_at TEXT
        );
        CREATE TABLE IF NOT EXISTS witnesses (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            case_id INTEGER NOT NULL,
            name TEXT NOT NULL,
            side TEXT NOT NULL,
            role TEXT,
            contact_info TEXT,
            subpoena_status TEXT,
            outline_notes TEXT,
            notes TEXT,
            created_at TEXT,
            updated_at TEXT
        );
        CREATE TABLE IF NOT EXISTS witness_persons (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL,
            contact_info TEXT,
            created_at TEXT,
            updated_at TEXT
        );
        CREATE TABLE IF NOT EXISTS exhibits (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            case_id INTEGER NOT NULL,
            label TEXT NOT NULL,
            side TEXT NOT NULL,
            description TEXT,
            status TEXT,
            notes TEXT,
            created_at TEXT,
            updated_at TEXT
        );
        CREATE TABLE IF NOT EXISTS trial_motions (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            case_id INTEGER NOT NULL,
            title TEXT NOT NULL,
            filed_by TEXT NOT NULL,
            filed_date TEXT,
            status TEXT,
            notes TEXT,
            created_at TEXT,
            updated_at TEXT
        );
        CREATE TABLE IF NOT EXISTS issue_tags (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL,
            description TEXT,
            category TEXT
        );
        CREATE TABLE IF NOT EXISTS case_issue_tags (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            case_id INTEGER NOT NULL,
            issue_tag_id INTEGER NOT NULL,
            notes TEXT,
            created_at TEXT,
            updated_at TEXT
        );
        CREATE TABLE IF NOT EXISTS document_exports (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            case_id INTEGER NOT NULL,
            document_type TEXT NOT NULL,
            document_title TEXT NOT NULL,
            output_path TEXT NOT NULL,
            created_at TEXT NOT NULL,
            status TEXT,
            qa_status TEXT,
            qa_notes TEXT,
            error_message TEXT,
            content_text TEXT,
            base_template_version TEXT,
            issue_tag_versions TEXT,
            is_draft INTEGER DEFAULT 0,
            is_finalized INTEGER DEFAULT 1,
            merge_field_values TEXT
        );
        CREATE TABLE IF NOT EXISTS case_notes (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            case_id INTEGER NOT NULL,
            title TEXT,
            body TEXT NOT NULL,
            created_at TEXT,
            updated_at TEXT
        );
        CREATE TABLE IF NOT EXISTS hearings (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            case_id INTEGER NOT NULL,
            title TEXT,
            hearing_date TEXT,
            location TEXT,
            description TEXT,
            created_at TEXT,
            updated_at TEXT,
            event_type TEXT NOT NULL DEFAULT 'Hearing',
            status TEXT NOT NULL DEFAULT 'Scheduled'
        );
        CREATE TABLE IF NOT EXISTS checklist_templates (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL,
            trigger_type TEXT NOT NULL,
            stage TEXT,
            issue_tag_name TEXT,
            case_type TEXT NOT NULL DEFAULT 'Any',
            track TEXT NOT NULL DEFAULT 'Any',
            active INTEGER DEFAULT 1,
            created_at TEXT,
            updated_at TEXT
        );
        CREATE TABLE IF NOT EXISTS checklist_template_items (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            template_id INTEGER NOT NULL,
            task TEXT NOT NULL,
            phase TEXT,
            sort_order INTEGER DEFAULT 0,
            due_offset_days INTEGER,
            created_at TEXT,
            updated_at TEXT
        );
        CREATE TABLE IF NOT EXISTS app_settings (
            key TEXT PRIMARY KEY,
            value TEXT,
            updated_at TEXT
        );
        CREATE TABLE IF NOT EXISTS risk_analyses (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            case_id INTEGER NOT NULL UNIQUE,
            narrative TEXT,
            rows_json TEXT NOT NULL,
            analysis_date TEXT,
            interest_rate REAL NOT NULL DEFAULT 0.06,
            contingency_fee_percent REAL NOT NULL DEFAULT 0.30,
            created_at TEXT,
            updated_at TEXT
        );
        CREATE TABLE IF NOT EXISTS risk_analysis_history (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            case_id INTEGER NOT NULL,
            analysis_date TEXT NOT NULL,
            formula_version TEXT NOT NULL DEFAULT 'risk-v1',
            narrative TEXT,
            rows_json TEXT NOT NULL,
            interest_rate REAL NOT NULL DEFAULT 0.06,
            contingency_fee_percent REAL NOT NULL DEFAULT 0.30,
            key_scenario_label TEXT,
            key_scenario_value REAL,
            key_scenario_order INTEGER,
            created_at TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS risk_analysis_offer_log (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            case_id INTEGER NOT NULL,
            offer_date TEXT,
            party TEXT,
            amount REAL,
            created_at TEXT,
            updated_at TEXT
        );
        CREATE TABLE IF NOT EXISTS service_log_entries (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            case_id INTEGER NOT NULL,
            party_name TEXT NOT NULL,
            status TEXT NOT NULL DEFAULT 'Not Served',
            method TEXT,
            event_date TEXT,
            notes TEXT,
            created_at TEXT,
            updated_at TEXT
        );
        CREATE TABLE IF NOT EXISTS discovery_base_versions (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            document_type TEXT NOT NULL,
            version INTEGER NOT NULL,
            content TEXT NOT NULL,
            active INTEGER NOT NULL DEFAULT 1,
            created_at TEXT NOT NULL,
            created_by TEXT
        );
        CREATE TABLE IF NOT EXISTS discovery_postures (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            case_id INTEGER NOT NULL UNIQUE,
            strategy TEXT,
            strategy_reason TEXT,
            strategy_selected_date TEXT,
            discovery_served_date TEXT,
            responses_due_date TEXT,
            responses_received_date TEXT,
            responses_reviewed_date TEXT,
            discovery_cutoff_date TEXT,
            planned_depositions TEXT,
            deficiency_status TEXT,
            next_decision TEXT,
            next_review_date TEXT,
            is_complete INTEGER DEFAULT 0,
            created_at TEXT,
            updated_at TEXT
        );
        CREATE TABLE IF NOT EXISTS pipeline_handoffs (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            case_id INTEGER NOT NULL,
            previous_holder TEXT,
            new_holder TEXT,
            previous_stage TEXT,
            new_stage TEXT,
            handoff_date TEXT,
            next_review_date TEXT,
            note TEXT,
            created_at TEXT
        );
        CREATE TABLE IF NOT EXISTS activity_log (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            case_id INTEGER NOT NULL,
            activity_type TEXT NOT NULL,
            is_meaningful INTEGER DEFAULT 1,
            occurred_at TEXT NOT NULL,
            notes TEXT,
            created_at TEXT
        );
        CREATE TABLE IF NOT EXISTS activity_log_history (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            activity_id INTEGER NOT NULL,
            previous_type TEXT,
            new_type TEXT,
            previous_occurred_at TEXT,
            new_occurred_at TEXT,
            previous_notes TEXT,
            new_notes TEXT,
            reason TEXT,
            created_at TEXT
        );
        CREATE INDEX IF NOT EXISTS idx_discovery_postures_case_id ON discovery_postures(case_id);
        CREATE INDEX IF NOT EXISTS idx_activity_log_case_id ON activity_log(case_id);
        CREATE INDEX IF NOT EXISTS idx_activity_log_history_activity_id ON activity_log_history(activity_id);
        """;

    // These indexes touch cases columns added via AddColumnIfMissingAsync below, so they must run
    // after EnsureSchemaUpgradesAsync's ALTER TABLE calls, not inside SchemaSql (which runs first,
    // against a cases table that may still be missing these columns on an existing database).
    private const string CasesDashboardIndexSql = """
        CREATE INDEX IF NOT EXISTS idx_cases_matter_type ON cases(matter_type);
        CREATE INDEX IF NOT EXISTS idx_cases_current_holder ON cases(current_holder);
        CREATE INDEX IF NOT EXISTS idx_cases_pipeline_stage ON cases(pipeline_stage);
        CREATE INDEX IF NOT EXISTS idx_cases_next_review_date ON cases(next_review_date);
        CREATE INDEX IF NOT EXISTS idx_cases_last_meaningful_activity_date ON cases(last_meaningful_activity_date);
        CREATE INDEX IF NOT EXISTS idx_cases_momentum_status ON cases(momentum_status);
        CREATE INDEX IF NOT EXISTS idx_cases_trial_date ON cases(trial_date);
        CREATE INDEX IF NOT EXISTS idx_cases_trial_track ON cases(trial_track);
        CREATE INDEX IF NOT EXISTS idx_cases_job_number ON cases(job_number);
        """;

    private static object DbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    private static string? BlankToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateOnly.TryParse(value, out var parsed))
        {
            return parsed == new DateOnly(1900, 1, 1) ? null : parsed.ToString("yyyy-MM-dd");
        }

        return null;
    }

    private static DateOnly? ParseDate(string? value)
    {
        if (DateOnly.TryParse(value, out var parsed))
        {
            return parsed == new DateOnly(1900, 1, 1) ? null : parsed;
        }

        return null;
    }

    private static string DisplayDate(string? value) => NormalizeDate(value) ?? "Not set";
    private static string DisplayTimestamp(string? value) =>
        DateTime.TryParse(value, out var parsed) ? parsed.ToLocalTime().ToString("g") : "Unknown";

    private static decimal? ParseMoney(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = value.Replace("$", "").Replace(",", "").Trim();
        return decimal.TryParse(cleaned, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static bool ParseBool(string? value, bool defaultValue = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "y" => true,
            "0" or "false" or "no" or "n" => false,
            _ => defaultValue
        };
    }

    private static string GetField(string[] row, Dictionary<string, int> map, string name)
    {
        if (!map.TryGetValue(name, out var index) || index < 0 || index >= row.Length)
        {
            return "";
        }

        return row[index]?.Trim() ?? "";
    }

    private static string SanitizeFileSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(ch => invalid.Contains(ch) ? '_' : ch)).Replace(' ', '_');
    }
}


public sealed class DuplicateIssueTagException : Exception
{
    public DuplicateIssueTagException(string message) : base(message)
    {
    }
}

