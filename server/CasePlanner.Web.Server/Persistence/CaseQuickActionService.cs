using System.Data.Common;
using CasePlanner.Data;
using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Security;
using CasePlanner.Web.Server.Services;
using Microsoft.Data.SqlClient;

namespace CasePlanner.Web.Server.Persistence;

public interface ICaseQuickActionService
{
    string Provider { get; }
    Task<string?> SetNextActionAsync(long caseId, SetNextActionRequest request, CancellationToken token = default);
    Task<string?> SetWaitingAsync(long caseId, SetWaitingRequest request, CancellationToken token = default);
    Task<string?> ClearWaitingAsync(long caseId, string? rowVersion, CancellationToken token = default);
    Task<string?> DeferAsync(long caseId, DeferActionRequest request, CancellationToken token = default);
    Task<string?> ClearDefermentAsync(long caseId, string? rowVersion, string? reason, CancellationToken token = default);
    Task<Dictionary<long, string?>> BulkDeferAsync(BulkDeferActionRequest request, CancellationToken token = default);
    Task<string?> SetHolderAsync(long caseId, SetHolderRequest request, CancellationToken token = default);
    Task<string?> SetPriorityAsync(long caseId, SetPriorityRequest request, CancellationToken token = default);
    Task<string?> SetTrialTrackAsync(long caseId, SetTrialTrackRequest request, CancellationToken token = default);
    Task<string?> SetShortNoteAsync(long caseId, ShortNoteRequest request, CancellationToken token = default);
}

public sealed class SqliteCaseQuickActionService(CasePlannerRepository repository) : ICaseQuickActionService
{
    public string Provider => "Sqlite";
    public async Task<string?> SetNextActionAsync(long caseId, SetNextActionRequest request, CancellationToken token = default) { await repository.SetNextActionAsync(caseId, request); return null; }
    public async Task<string?> SetWaitingAsync(long caseId, SetWaitingRequest request, CancellationToken token = default) { await repository.SetWaitingAsync(caseId, request); return null; }
    public async Task<string?> ClearWaitingAsync(long caseId, string? rowVersion, CancellationToken token = default) { await repository.ClearWaitingAsync(caseId); return null; }
    public async Task<string?> DeferAsync(long caseId, DeferActionRequest request, CancellationToken token = default) { await repository.DeferActionAsync(caseId, request); return null; }
    public async Task<string?> ClearDefermentAsync(long caseId, string? rowVersion, string? reason, CancellationToken token = default) { await repository.ClearDefermentAsync(caseId, reason); return null; }
    public async Task<Dictionary<long, string?>> BulkDeferAsync(BulkDeferActionRequest request, CancellationToken token = default)
    {
        await repository.BulkDeferActionAsync(request.CaseIds, new DeferActionRequest { Reason = request.Reason, FutureReviewDate = request.FutureReviewDate });
        return request.CaseIds.ToDictionary(id => id, _ => (string?)null);
    }
    public async Task<string?> SetHolderAsync(long caseId, SetHolderRequest request, CancellationToken token = default) { await repository.SetHolderAsync(caseId, request); return null; }
    public async Task<string?> SetPriorityAsync(long caseId, SetPriorityRequest request, CancellationToken token = default) { await repository.SetPriorityAsync(caseId, request); return null; }
    public async Task<string?> SetTrialTrackAsync(long caseId, SetTrialTrackRequest request, CancellationToken token = default) { await repository.SetTrialTrackAsync(caseId, request); return null; }
    public async Task<string?> SetShortNoteAsync(long caseId, ShortNoteRequest request, CancellationToken token = default) { await repository.SetShortNoteAsync(caseId, request.Note); return null; }
}

public sealed class SqlServerCaseQuickActionService(
    IDatabaseConnectionFactory connections,
    IApplicationActorContext actor,
    SqlServerActivityStore activity) : ICaseQuickActionService
{
    public string Provider => "SqlServer";

    public async Task<string?> SetNextActionAsync(long caseId, SetNextActionRequest request, CancellationToken token = default)
    {
        var version = await UpdateAsync(caseId, request.RowVersion,
            "next_action=@nextAction,next_review_date=@review,next_action_due=@review",
            command =>
            {
                command.Parameters.Add(new SqlParameter("@nextAction", Db(request.NextAction)));
                command.Parameters.Add(new SqlParameter("@review", Db(Date(request.NextReviewDate))));
            }, "CaseNextActionSet", token);
        await activity.RecordAsync(caseId, "NextActionSet", request.NextAction, null, token);
        return version;
    }

    public async Task<string?> SetWaitingAsync(long caseId, SetWaitingRequest request, CancellationToken token = default)
    {
        var version = await UpdateAsync(caseId, request.RowVersion,
            "waiting_on=@waitingOn,waiting_reason=@reason,waiting_started_date=@started,expected_response=@response,waiting_follow_up_date=@followUp,waiting_escalation_action=@escalation",
            command =>
            {
                command.Parameters.Add(new SqlParameter("@waitingOn", request.WaitingOn ?? ""));
                command.Parameters.Add(new SqlParameter("@reason", Db(request.WaitingReason)));
                command.Parameters.Add(new SqlParameter("@started", Db(Date(request.WaitingStartedDate))));
                command.Parameters.Add(new SqlParameter("@response", Db(request.ExpectedResponse)));
                command.Parameters.Add(new SqlParameter("@followUp", Db(Date(request.WaitingFollowUpDate))));
                command.Parameters.Add(new SqlParameter("@escalation", Db(request.WaitingEscalationAction)));
            }, "CaseMarkedWaiting", token);
        await activity.RecordAsync(caseId, "MarkedWaiting", $"Waiting on {request.WaitingOn}", null, token);
        return version;
    }

    public async Task<string?> ClearWaitingAsync(long caseId, string? rowVersion, CancellationToken token = default) =>
        await UpdateAsync(caseId, rowVersion,
            "waiting_on=NULL,waiting_reason=NULL,waiting_started_date=NULL,expected_response=NULL,waiting_follow_up_date=NULL,waiting_escalation_action=NULL",
            _ => { }, "CaseWaitingCleared", token);

    public async Task<string?> DeferAsync(long caseId, DeferActionRequest request, CancellationToken token = default)
    {
        if (!DateOnly.TryParse(request.FutureReviewDate, out var until)) throw new InvalidOperationException("A valid defer-until date is required.");
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        var version = await UpdateAsync(caseId, request.RowVersion,
            "deferred_until=@until,deferred_reason=@reason,deferred_at=@at,deferred_by=@by",
            command =>
            {
                command.Parameters.Add(new SqlParameter("@until", until.ToString("yyyy-MM-dd")));
                command.Parameters.Add(new SqlParameter("@reason", Db(reason)));
                command.Parameters.Add(new SqlParameter("@at", DateTime.UtcNow.ToString("O")));
                command.Parameters.Add(new SqlParameter("@by", actor.AuditLabel));
            }, "CaseDeferred", token);
        var detail = reason is null ? $"Deferred until {until:yyyy-MM-dd}" : $"Deferred until {until:yyyy-MM-dd}: {reason}";
        await activity.RecordAsync(caseId, "CaseDeferred", detail, null, token);
        return version;
    }

    public async Task<string?> ClearDefermentAsync(long caseId, string? rowVersion, string? reason, CancellationToken token = default)
    {
        var version = await UpdateAsync(caseId, rowVersion,
            "deferred_until=NULL,deferred_reason=NULL,deferred_at=NULL,deferred_by=NULL",
            _ => { }, "CaseDefermentCleared", token);
        await activity.RecordAsync(caseId, "CaseDefermentCleared",
            string.IsNullOrWhiteSpace(reason) ? "Deferment cleared" : $"Deferment cleared: {reason.Trim()}", null, token);
        return version;
    }

    public async Task<Dictionary<long, string?>> BulkDeferAsync(BulkDeferActionRequest request, CancellationToken token = default)
    {
        var result = new Dictionary<long, string?>();
        foreach (var caseId in request.CaseIds)
        {
            request.RowVersions.TryGetValue(caseId, out var rowVersion);
            result[caseId] = await DeferAsync(caseId, new DeferActionRequest
            {
                RowVersion = rowVersion,
                Reason = request.Reason,
                FutureReviewDate = request.FutureReviewDate
            }, token);
        }
        return result;
    }

    // Not routed through the shared UpdateAsync helper below - it needs the case's previous
    // holder/stage and the pipeline_handoffs insert to happen inside the same transaction as the
    // update, the same reason SqlServerPipelineHandoffStore.SaveAsync manages its own transaction.
    public async Task<string?> SetHolderAsync(long caseId, SetHolderRequest request, CancellationToken token = default)
    {
        var expected = ExpectedVersion(request.RowVersion, caseId);
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);

        string? previousHolder; string? previousStage;
        await using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT current_holder,pipeline_stage FROM dbo.cases WHERE id=@id AND row_version=@version AND is_deleted=0";
            read.Parameters.Add(new SqlParameter("@id", caseId));
            read.Parameters.Add(new SqlParameter("@version", expected));
            await using var reader = await read.ExecuteReaderAsync(token);
            if (!await reader.ReadAsync(token)) throw new CaseConcurrencyException(caseId);
            previousHolder = reader.IsDBNull(0) ? null : reader.GetString(0);
            previousStage = reader.IsDBNull(1) ? null : reader.GetString(1);
        }

        string version;
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE dbo.cases SET current_holder=@holder,date_sent_to_current_holder=@sent,updated_at=@updated OUTPUT INSERTED.row_version WHERE id=@id AND row_version=@version AND is_deleted=0";
            update.Parameters.Add(new SqlParameter("@holder", Db(request.CurrentHolder)));
            update.Parameters.Add(new SqlParameter("@sent", DateTime.UtcNow.ToString("yyyy-MM-dd")));
            update.Parameters.Add(new SqlParameter("@updated", DateTime.UtcNow.ToString("O")));
            update.Parameters.Add(new SqlParameter("@id", caseId));
            update.Parameters.Add(new SqlParameter("@version", expected));
            var value = await update.ExecuteScalarAsync(token);
            if (value is null) throw new CaseConcurrencyException(caseId);
            version = Convert.ToBase64String((byte[])value);
        }

        await PipelineHandoffTransitionLogger.RecordIfChangedAsync(
            connection, transaction, caseId, previousHolder, request.CurrentHolder, previousStage, previousStage,
            actor.UserId, actor.AuditLabel, token);

        await using (var audit = connection.CreateCommand())
        {
            audit.Transaction = transaction;
            audit.CommandText = "INSERT INTO dbo.audit_events(case_id,actor_user_id,action,entity_type,entity_id) VALUES(@caseId,@actor,'CaseHolderAssigned','Case',@id)";
            audit.Parameters.Add(new SqlParameter("@caseId", caseId));
            audit.Parameters.Add(new SqlParameter("@actor", (object?)actor.UserId ?? DBNull.Value));
            audit.Parameters.Add(new SqlParameter("@id", caseId.ToString()));
            await audit.ExecuteNonQueryAsync(token);
        }

        await transaction.CommitAsync(token);
        await activity.RecordAsync(caseId, "HolderAssigned", $"Assigned to {request.CurrentHolder}", null, token);
        return version;
    }

    public async Task<string?> SetPriorityAsync(long caseId, SetPriorityRequest request, CancellationToken token = default) =>
        await UpdateAsync(caseId, request.RowVersion, "priority=@priority",
            command => command.Parameters.Add(new SqlParameter("@priority", string.IsNullOrWhiteSpace(request.Priority) ? "Normal" : request.Priority)),
            "CasePrioritySet", token);

    public async Task<string?> SetTrialTrackAsync(long caseId, SetTrialTrackRequest request, CancellationToken token = default) =>
        await UpdateAsync(caseId, request.RowVersion, "trial_track=@trialTrack",
            command => command.Parameters.Add(new SqlParameter("@trialTrack", request.TrialTrack)),
            "CaseTrialTrackSet", token);

    public async Task<string?> SetShortNoteAsync(long caseId, ShortNoteRequest request, CancellationToken token = default)
    {
        var version = await UpdateAsync(caseId, request.RowVersion, "short_posture_summary=@note",
            command => command.Parameters.Add(new SqlParameter("@note", Db(request.Note))), "CaseShortNoteSet", token);
        await activity.RecordAsync(caseId, "ShortNoteAdded", request.Note, null, token);
        return version;
    }

    private async Task<string> UpdateAsync(
        long caseId, string? rowVersion, string setClause, Action<DbCommand> bind, string action, CancellationToken token)
    {
        var expected = ExpectedVersion(rowVersion, caseId);
        await using var connection = connections.CreateConnection();
        await connection.OpenAsync(token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"UPDATE dbo.cases SET {setClause},updated_at=@updated OUTPUT INSERTED.row_version WHERE id=@id AND row_version=@version AND is_deleted=0";
        command.Parameters.Add(new SqlParameter("@updated", DateTime.UtcNow.ToString("O")));
        command.Parameters.Add(new SqlParameter("@id", caseId));
        command.Parameters.Add(new SqlParameter("@version", expected));
        bind(command);
        var value = await command.ExecuteScalarAsync(token);
        if (value is null) throw new CaseConcurrencyException(caseId);
        await using var audit = connection.CreateCommand();
        audit.Transaction = transaction;
        audit.CommandText = "INSERT INTO dbo.audit_events(case_id,actor_user_id,action,entity_type,entity_id) VALUES(@caseId,@actor,@action,'Case',@id)";
        audit.Parameters.Add(new SqlParameter("@caseId", caseId));
        audit.Parameters.Add(new SqlParameter("@actor", (object?)actor.UserId ?? DBNull.Value));
        audit.Parameters.Add(new SqlParameter("@action", action));
        audit.Parameters.Add(new SqlParameter("@id", caseId.ToString()));
        await audit.ExecuteNonQueryAsync(token);
        await transaction.CommitAsync(token);
        return Convert.ToBase64String((byte[])value);
    }

    private static byte[] ExpectedVersion(string? value, long caseId)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"RowVersion is required when changing SQL Server case {caseId}.");
        try { return Convert.FromBase64String(value); }
        catch (FormatException) { throw new ArgumentException("RowVersion is not a valid concurrency token."); }
    }

    private static object Db(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    private static string? Date(string? value) => DateOnly.TryParse(value, out var date) ? date.ToString("yyyy-MM-dd") : null;
}
