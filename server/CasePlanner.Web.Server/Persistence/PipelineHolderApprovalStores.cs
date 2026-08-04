using CasePlanner.Data;
using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Security;
using CasePlanner.Web.Server.Services;
using Microsoft.Data.SqlClient;

namespace CasePlanner.Web.Server.Persistence;

// Manager Dashboard sign-off consolidation, item 1: the pre-suit intake chain (Legal Assistant ->
// Attorney -> Deputy Chief Counsel -> Chief Counsel) is now pure fact/history, not a gate. Moving
// CurrentHolder to any value - forward, backward, or lateral, gated role or not - is never blocked
// here; PipelineHolderApprovalRecord rows (Approved/Returned) and pipeline_handoffs entries remain
// as a record of who held the file when, but nothing downstream requires an "Approved" row to
// exist before the file can move on. This replaces the former RequiresApproval/EnsureApproved gate
// that used to sit in CasePlannerRepository.SetHolderAsync/SqlServerCaseQuickActionService.SetHolderAsync -
// removed entirely, not just relaxed, so Chief Counsel (or anyone else in the chain) never needs to
// take any in-system action for a file to advance past them.
//
// A second leanness pass removed this class's RequiresFilingApproval/EnsureFilingReady members as
// dead code - zero real callers anywhere (RequiresFilingApproval was never actually invoked, and
// EnsureFilingReady had already been reduced to a documented no-op). Leaving Pipeline status has no
// in-system gate today; see CaseRecord.FilingGateOverrideReason for the still-live, non-blocking
// audit-context field this left behind. GatedChain stays - it's still real, used below to derive
// step-state for the client's HolderPipelineStepper and to compute "the role immediately before
// this one" for the Return for Revision convenience action, just no longer for any gating decision.
internal static class PipelinePromotionGate
{
    public static readonly string[] GatedChain = ["Legal Assistant", "Attorney", "Deputy Chief Counsel", "Chief Counsel"];
}

// Manager/Administrator Dashboard Milestone 4 correction: enforces the strict sequential order of
// the four pre-filing milestones case_prefiling_milestones tracks. Kept in this file (rather than a
// separate one) because PipelineHolderApprovalStores.cs is this codebase's established home for
// pipeline-related gate logic, even though this gate is otherwise unrelated to
// PipelinePromotionGate's holder-chain/filing-approval gates above.
internal static class PreFilingMilestoneGate
{
    // Strict order: marking milestone N requires milestone N-1 to already be marked (except
    // PleadingsPackageSent, which has no prerequisite); un-marking milestone N requires that no
    // milestone AFTER it is currently marked.
    public static readonly string[] Order =
    [
        "PleadingsPackageSent",
        "ChiefCounselSignaturesReceived",
        "DeclarationOfTakingSentToDirector",
        "DirectorSignatureReceived",
    ];

    // "PleadingsPackageSent" -> "Pleadings Package Sent", etc., for readable error messages and
    // exception text - never persisted, purely a display/message helper.
    public static string Label(string milestone) => milestone switch
    {
        "PleadingsPackageSent" => "Pleadings Package Sent",
        "ChiefCounselSignaturesReceived" => "Chief Counsel Signatures Received",
        "DeclarationOfTakingSentToDirector" => "Declaration of Taking Sent to Director",
        "DirectorSignatureReceived" => "Director Signature Received",
        _ => milestone,
    };

    // currentlyMarked: the case's existing milestone rows, keyed by milestone name, true if
    // is_marked. A milestone absent from the dictionary is treated as not marked (a case with none
    // of the four rows yet on file passes an empty dictionary here).
    public static void EnsureCanMark(string milestone, IReadOnlyDictionary<string, bool> currentlyMarked)
    {
        var index = Array.IndexOf(Order, milestone);
        if (index < 0) throw new ArgumentException($"Milestone must be one of: {string.Join(", ", Order)}.");
        if (currentlyMarked.TryGetValue(milestone, out var already) && already)
            throw new InvalidOperationException($"\"{Label(milestone)}\" is already marked.");
        if (index > 0)
        {
            var prerequisite = Order[index - 1];
            if (!currentlyMarked.TryGetValue(prerequisite, out var prereqMarked) || !prereqMarked)
                throw new InvalidOperationException($"{Label(prerequisite)} must be marked before {Label(milestone)}.");
        }
    }

    public static void EnsureCanUnmark(string milestone, IReadOnlyDictionary<string, bool> currentlyMarked)
    {
        var index = Array.IndexOf(Order, milestone);
        if (index < 0) throw new ArgumentException($"Milestone must be one of: {string.Join(", ", Order)}.");
        if (!currentlyMarked.TryGetValue(milestone, out var already) || !already)
            throw new InvalidOperationException($"\"{Label(milestone)}\" is not currently marked.");
        for (var i = index + 1; i < Order.Length; i++)
        {
            if (currentlyMarked.TryGetValue(Order[i], out var laterMarked) && laterMarked)
                throw new InvalidOperationException($"{Label(Order[i])} is still marked; unmark it before unmarking {Label(milestone)}.");
        }
    }

    // Shared by CasePlannerRepository.GetPreFilingMilestoneAgingAsync (SQLite) and
    // SqlServerPreFilingMilestoneStore.GetAgingAsync so the "furthest marked milestone" derivation
    // lives in exactly one place. pipelineCases: every case currently in CaseStatus="Pipeline".
    // markedAtByCase: case id -> (milestone -> MarkedAt), containing only rows where is_marked=1.
    public static PreFilingMilestoneAgingSummary BuildAgingSummary(
        IEnumerable<(long CaseId, string? JobNumber, string? Tract, string? CaseName)> pipelineCases,
        IReadOnlyDictionary<long, Dictionary<string, string?>> markedAtByCase)
    {
        var summary = new PreFilingMilestoneAgingSummary();
        var bucketCounts = new Dictionary<string, int> { ["None"] = 0 };
        foreach (var m in Order) bucketCounts[m] = 0;
        var now = DateTime.UtcNow;

        foreach (var c in pipelineCases)
        {
            var furthest = "None";
            string? markedAt = null;
            if (markedAtByCase.TryGetValue(c.CaseId, out var marks))
            {
                for (var i = Order.Length - 1; i >= 0; i--)
                {
                    if (marks.TryGetValue(Order[i], out var at) && at is not null)
                    {
                        furthest = Order[i];
                        markedAt = at;
                        break;
                    }
                }
            }

            bucketCounts[furthest] = bucketCounts.GetValueOrDefault(furthest) + 1;
            int? days = null;
            if (markedAt is not null && DateTime.TryParse(markedAt, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var markedDate))
            {
                days = (int)(now - markedDate).TotalDays;
            }

            summary.Cases.Add(new PreFilingMilestoneAgingCase
            {
                CaseId = c.CaseId,
                JobNumber = c.JobNumber,
                Tract = c.Tract,
                CaseName = c.CaseName,
                FurthestMilestone = furthest,
                DaysSinceMarked = days,
            });
        }

        summary.Buckets = bucketCounts.Select(kv => new PreFilingMilestoneAgingBucket { Milestone = kv.Key, Count = kv.Value }).ToList();
        return summary;
    }
}

// pipeline_holder_approvals - append-only log backing PipelinePromotionGate above. Every
// Approve/Return-for-Revision action inserts a NEW row rather than updating an existing one, so
// history survives a cycle like Approved -> Returned -> re-Approved. Mirrors ICaseDefendantStore's
// provider-selected store pattern, but read/insert only (no update, no delete) since nothing here
// is ever edited or removed.
public interface IPipelineHolderApprovalStore
{
    string Provider { get; }
    Task<List<PipelineHolderApprovalRecord>> GetAsync(long? caseId, CancellationToken token = default);
    Task<PipelineHolderApprovalRecord> RecordAsync(PipelineHolderApprovalRecord model, CancellationToken token = default);
}

public sealed class SqlitePipelineHolderApprovalStore(CasePlannerRepository repository) : IPipelineHolderApprovalStore
{
    public string Provider => "Sqlite";
    public Task<List<PipelineHolderApprovalRecord>> GetAsync(long? caseId, CancellationToken token = default) => repository.GetPipelineHolderApprovalsAsync(caseId);
    public Task<PipelineHolderApprovalRecord> RecordAsync(PipelineHolderApprovalRecord model, CancellationToken token = default) => repository.RecordPipelineHolderApprovalAsync(model);
}

// SQL Server side of the pipeline_holder_approvals table. There is no live SQL Server sandbox
// available here to exercise this against a real pilot instance - same caveat already noted for
// the rest of the dormant multi-user foundation.
public sealed class SqlServerPipelineHolderApprovalStore(IDatabaseConnectionFactory connections, IHttpContextAccessor accessor)
    : SqlServerLitigationStoreBase(connections, accessor), IPipelineHolderApprovalStore
{
    public string Provider => "SqlServer";

    public async Task<List<PipelineHolderApprovalRecord>> GetAsync(long? caseId, CancellationToken token = default)
    {
        var result = new List<PipelineHolderApprovalRecord>();
        await using var connection = Connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,case_id,holder_role,status,note,set_at,set_by_display_name FROM dbo.pipeline_holder_approvals WHERE (@caseId IS NULL OR case_id=@caseId) ORDER BY id DESC";
        command.Parameters.Add(new SqlParameter("@caseId", (object?)caseId ?? DBNull.Value));
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(new()
        {
            Id = reader.GetInt64(0),
            CaseId = reader.GetInt64(1),
            HolderRole = Text(reader, 2) ?? "",
            Status = Text(reader, 3) ?? "",
            Note = Text(reader, 4),
            SetAt = Text(reader, 5) ?? "",
            SetByDisplayName = Text(reader, 6),
        });
        return result;
    }

    public async Task<PipelineHolderApprovalRecord> RecordAsync(PipelineHolderApprovalRecord model, CancellationToken token = default)
    {
        await using var connection = Connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await EnsureCaseExistsAsync(connection, transaction, model.CaseId, token);
        var now = DateTime.UtcNow.ToString("O");
        model.SetAt = string.IsNullOrWhiteSpace(model.SetAt) ? now : model.SetAt;
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO dbo.pipeline_holder_approvals (case_id,holder_role,status,note,set_at,set_by_display_name)
                OUTPUT INSERTED.id
                VALUES (@caseId,@role,@status,@note,@setAt,@setBy)
                """;
            insert.Parameters.Add(new SqlParameter("@caseId", model.CaseId));
            insert.Parameters.Add(new SqlParameter("@role", model.HolderRole));
            insert.Parameters.Add(new SqlParameter("@status", model.Status));
            insert.Parameters.Add(new SqlParameter("@note", Db(model.Note)));
            insert.Parameters.Add(new SqlParameter("@setAt", model.SetAt));
            insert.Parameters.Add(new SqlParameter("@setBy", Db(model.SetByDisplayName)));
            model.Id = Convert.ToInt64(await insert.ExecuteScalarAsync(token));
        }
        await AuditAsync(connection, transaction, model.CaseId, "PipelineHolderApprovalRecorded", "PipelineHolderApproval", model.Id, token);
        await transaction.CommitAsync(token);
        return model;
    }
}

// Task C: the client-facing Approve / Return for Revision action behind POST
// /api/cases/{id}/pipeline-approvals. Provider-neutral - it composes the already
// provider-selected IPipelineHolderApprovalStore/ICaseQuickActionService/ICaseCatalogReader
// rather than duplicating the "insert the log row, then maybe move the holder back or stamp the
// waiting fields" orchestration once per database provider (mirrors
// ProviderNeutralCaseNotesExportService's shape in CaseNotesExportService.cs).
public interface IPipelineHolderApprovalActionService
{
    string Provider { get; }
    Task<List<PipelineHolderApprovalRecord>> GetAsync(long? caseId, CancellationToken token = default);
    Task<PipelineHolderApprovalRecord> RecordAsync(long caseId, RecordPipelineHolderApprovalRequest request, CancellationToken token = default);
}

public sealed class ProviderNeutralPipelineHolderApprovalActionService(
    IPipelineHolderApprovalStore store,
    ICaseQuickActionService quickActions,
    ICaseCatalogReader cases,
    IApplicationActorContext actor) : IPipelineHolderApprovalActionService
{
    public string Provider => store.Provider;

    public Task<List<PipelineHolderApprovalRecord>> GetAsync(long? caseId, CancellationToken token = default) => store.GetAsync(caseId, token);

    public async Task<PipelineHolderApprovalRecord> RecordAsync(long caseId, RecordPipelineHolderApprovalRequest request, CancellationToken token = default)
    {
        if (request.Status is not ("Approved" or "Returned"))
            throw new ArgumentException("Status must be \"Approved\" or \"Returned\".");
        var chainIndex = Array.IndexOf(PipelinePromotionGate.GatedChain, request.HolderRole);
        if (chainIndex < 0)
            throw new ArgumentException($"HolderRole must be one of: {string.Join(", ", PipelinePromotionGate.GatedChain)}.");

        var saved = await store.RecordAsync(new PipelineHolderApprovalRecord
        {
            CaseId = caseId,
            HolderRole = request.HolderRole,
            Status = request.Status,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            SetAt = DateTime.UtcNow.ToString("O"),
            // No real authentication yet (Entra ID is dormant) - free text, falling back to the
            // shared actor label when the client doesn't supply one. Same "records whoever the
            // client says acted, with no cryptographic proof" limitation
            // PipelineHandoffRecord.CreatedBy already carries.
            SetByDisplayName = string.IsNullOrWhiteSpace(request.SetByDisplayName) ? actor.AuditLabel : request.SetByDisplayName.Trim(),
        }, token);

        if (request.Status == "Returned")
        {
            if (chainIndex == 0)
                throw new ArgumentException($"\"{request.HolderRole}\" is the first step in the pipeline chain and has no prior holder to return to.");

            // Return for Revision moves the case back to whoever comes immediately before
            // HolderRole in the gated chain, through the exact same SetHolderAsync path the
            // stepper itself uses - so pipeline_handoffs history stays consistent and Task B's
            // gate (a forward-only check) is naturally a no-op for this backward move.
            var current = (await cases.GetCasesAsync(new CaseCatalogQuery(IncludeClosed: true), token)).FirstOrDefault(c => c.Id == caseId);
            await quickActions.SetHolderAsync(caseId, new SetHolderRequest
            {
                RowVersion = current?.RowVersion,
                CurrentHolder = PipelinePromotionGate.GatedChain[chainIndex - 1],
            }, token);
        }
        else if (request.HolderRole == "Chief Counsel")
        {
            // Chief Counsel's Approved status is also the moment the complaint is ready to file -
            // per the office's process it then goes upstairs for the Director of Highways and
            // Transportation to sign the Declaration of Taking, which is a wait for a signature
            // (not an approval - nothing to reject), so this reuses the case's existing
            // WaitingOn/WaitingStartedDate quick-action path rather than a new mechanism.
            var current = (await cases.GetCasesAsync(new CaseCatalogQuery(IncludeClosed: true), token)).FirstOrDefault(c => c.Id == caseId);
            await quickActions.SetWaitingAsync(caseId, new SetWaitingRequest
            {
                RowVersion = current?.RowVersion,
                WaitingOn = "Director of Highways and Transportation — Declaration of Taking signature",
                WaitingStartedDate = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            }, token);
        }

        return saved;
    }
}
