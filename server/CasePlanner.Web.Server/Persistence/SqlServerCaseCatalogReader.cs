using CasePlanner.Data;
using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Security;
using Microsoft.Data.SqlClient;

namespace CasePlanner.Web.Server.Persistence;

public sealed class CaseConcurrencyException(long caseId) : Exception($"Case {caseId} was changed by another user. Reload it before saving again.");

public sealed class SqlServerCaseCatalogReader(IDatabaseConnectionFactory connectionFactory, IHttpContextAccessor httpContextAccessor) : ICaseCatalogStore
{
    public string Provider => "SqlServer";

    public async Task<List<CaseRecord>> GetCasesAsync(CaseCatalogQuery query, CancellationToken cancellationToken = default)
    {
        var list = new List<CaseRecord>();
        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = SelectSql;
        command.Parameters.Add(new SqlParameter("@includeClosed", query.IncludeClosed));
        command.Parameters.Add(new SqlParameter("@search", query.Search));
        command.Parameters.Add(new SqlParameter("@like", $"%{query.Search}%"));
        command.Parameters.Add(new SqlParameter("@status", query.Status));
        command.Parameters.Add(new SqlParameter("@county", query.County));
        command.Parameters.Add(new SqlParameter("@stage", query.Stage));
        command.Parameters.Add(new SqlParameter("@track", query.Track));
        command.Parameters.Add(new SqlParameter("@caseStatus", query.CaseStatus));
        command.Parameters.Add(new SqlParameter("@dateOpenedFrom", query.DateOpenedFrom));
        command.Parameters.Add(new SqlParameter("@dateOpenedTo", query.DateOpenedTo));
        command.Parameters.Add(new SqlParameter("@dateClosedFrom", query.DateClosedFrom));
        command.Parameters.Add(new SqlParameter("@dateClosedTo", query.DateClosedTo));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) list.Add(CaseRecordDataMapper.Read(reader));
        return list;
    }

    public async Task<CaseRecord> SaveCaseAsync(CaseRecord model, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(model.CaseName)) throw new ArgumentException("Case name is required.");
        NormalizeForSave(model);
        var isNew = model.Id == 0;

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow.ToString("O");
        var values = Values(model, now);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        var assignments = values.Select((pair, index) => (pair.Key, Parameter: $"@p{index}", pair.Value)).ToList();
        foreach (var item in assignments)
            command.Parameters.Add(new SqlParameter(item.Parameter, item.Value ?? DBNull.Value));

        if (isNew)
        {
            command.CommandText = $"INSERT INTO dbo.cases ({string.Join(",", assignments.Select(a => $"[{a.Key}]"))}) OUTPUT INSERTED.id, INSERTED.row_version VALUES ({string.Join(",", assignments.Select(a => a.Parameter))})";
        }
        else
        {
            if (string.IsNullOrWhiteSpace(model.RowVersion)) throw new ArgumentException("RowVersion is required when updating a SQL Server case.");
            byte[] expected;
            try { expected = Convert.FromBase64String(model.RowVersion); }
            catch (FormatException) { throw new ArgumentException("RowVersion is not a valid concurrency token."); }
            command.Parameters.Add(new SqlParameter("@id", model.Id));
            command.Parameters.Add(new SqlParameter("@rowVersion", expected));
            command.CommandText = $"UPDATE dbo.cases SET {string.Join(",", assignments.Where(a => a.Key != "created_at").Select(a => $"[{a.Key}]={a.Parameter}"))} OUTPUT INSERTED.id, INSERTED.row_version WHERE id=@id AND row_version=@rowVersion";
        }

        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) throw new CaseConcurrencyException(model.Id);
            model.Id = reader.GetInt64(0);
            model.RowVersion = Convert.ToBase64String((byte[])reader.GetValue(1));
        }

        await using var audit = connection.CreateCommand();
        audit.Transaction = transaction;
        audit.CommandText = "INSERT INTO dbo.audit_events (case_id, actor_user_id, action, entity_type, entity_id, details_json) VALUES (@caseId,@actor,@action,'Case',@entityId,@details)";
        audit.Parameters.Add(new SqlParameter("@caseId", model.Id));
        audit.Parameters.Add(new SqlParameter("@actor", (object?)ActorUserId() ?? DBNull.Value));
        audit.Parameters.Add(new SqlParameter("@action", isNew ? "CaseCreated" : "CaseUpdated"));
        audit.Parameters.Add(new SqlParameter("@entityId", model.Id.ToString()));
        audit.Parameters.Add(new SqlParameter("@details", $"{{\"caseNumber\":\"{JsonEscape(model.CaseNumber)}\"}}"));
        await audit.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return model;
    }

    public async Task DeleteCaseAsync(long caseId, string? rowVersion = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rowVersion)) throw new ArgumentException("RowVersion is required when deleting a SQL Server case.");
        byte[] expected;
        try { expected = Convert.FromBase64String(rowVersion); }
        catch (FormatException) { throw new ArgumentException("RowVersion is not a valid concurrency token."); }

        await using var connection = connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE dbo.cases SET is_deleted=1, deleted_utc=SYSUTCDATETIME(), deleted_by_user_id=@actor, updated_at=@now OUTPUT INSERTED.id WHERE id=@id AND row_version=@rowVersion AND is_deleted=0";
        command.Parameters.Add(new SqlParameter("@now", DateTime.UtcNow.ToString("O")));
        command.Parameters.Add(new SqlParameter("@actor", (object?)ActorUserId() ?? DBNull.Value));
        command.Parameters.Add(new SqlParameter("@id", caseId));
        command.Parameters.Add(new SqlParameter("@rowVersion", expected));
        if (await command.ExecuteScalarAsync(cancellationToken) is null) throw new CaseConcurrencyException(caseId);

        await using var audit = connection.CreateCommand();
        audit.Transaction = transaction;
        audit.CommandText = "INSERT INTO dbo.audit_events (case_id, actor_user_id, action, entity_type, entity_id) VALUES (@caseId,@actor,'CaseDeleted','Case',@entityId)";
        audit.Parameters.Add(new SqlParameter("@caseId", caseId));
        audit.Parameters.Add(new SqlParameter("@actor", (object?)ActorUserId() ?? DBNull.Value));
        audit.Parameters.Add(new SqlParameter("@entityId", caseId.ToString()));
        await audit.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static Dictionary<string, object?> Values(CaseRecord model, string now) => new()
    {
        ["case_number"] = model.CaseNumber.Trim(), ["case_name"] = model.CaseName.Trim(),
        ["job_number"] = Null(model.JobNumber), ["tract"] = Null(model.Tract), ["county"] = Null(model.County),
        ["status"] = Null(model.Status), ["stage"] = Null(model.Stage), ["track"] = model.Track,
        ["filing_date"] = Null(model.FilingDate), ["date_of_taking"] = Null(model.DateOfTaking), ["trial_date"] = Null(model.TrialDate),
        ["next_action"] = Null(model.NextAction), ["next_action_due"] = Null(model.NextActionDue), ["deposit_amount"] = model.DepositAmount,
        ["owner"] = Null(model.Owner), ["landowner"] = Null(model.Landowner), ["valuation_notes"] = Null(model.ValuationNotes),
        ["settlement_notes"] = Null(model.SettlementNotes), ["publication_service_notes"] = Null(model.PublicationServiceNotes),
        ["service_required"] = model.ServiceRequired ? 1L : 0L, ["service_perfected"] = model.ServicePerfected ? 1L : 0L,
        ["service_perfected_date"] = Null(model.ServicePerfectedDate), ["service_deadline_120"] = Null(model.ServiceDeadline120),
        ["service_deadline_basis_date"] = Null(model.ServiceDeadlineBasisDate), ["service_method"] = Null(model.ServiceMethod),
        ["service_notes"] = Null(model.ServiceNotes), ["service_status"] = Null(model.ServiceStatus),
        ["assigned_attorney"] = Null(model.AssignedAttorney), ["opposing_counsel"] = Null(model.OpposingCounsel), ["appraiser"] = Null(model.Appraiser),
        ["taxes_owed"] = Null(model.TaxesOwed), ["funds_withdrawn"] = Null(model.FundsWithdrawn), ["funds_withdrawn_date"] = Null(model.FundsWithdrawnDate),
        ["discovery_completed"] = Null(model.DiscoveryCompleted), ["updated_appraisal"] = Null(model.UpdatedAppraisal), ["closed_date"] = Null(model.ClosedDate), ["date_opened"] = Null(model.DateOpened),
        ["project_name"] = Null(model.ProjectName), ["tax_owed_amount"] = model.TaxOwedAmount, ["whole_property_acres"] = model.WholePropertyAcres,
        ["acquisition_acres"] = model.AcquisitionAcres, ["landowner_appraiser_name"] = Null(model.LandownerAppraiserName),
        ["additional_deposit_amount"] = model.AdditionalDepositAmount, ["additional_deposit_date"] = Null(model.AdditionalDepositDate),
        ["matter_type"] = model.MatterType, ["priority"] = model.Priority, ["current_holder"] = Null(model.CurrentHolder),
        ["pipeline_stage"] = Null(model.PipelineStage), ["date_sent_to_current_holder"] = Null(model.DateSentToCurrentHolder),
        ["next_review_date"] = Null(model.NextReviewDate), ["momentum_status"] = Null(model.MomentumStatus),
        ["waiting_reason"] = Null(model.WaitingReason), ["waiting_on"] = Null(model.WaitingOn), ["waiting_started_date"] = Null(model.WaitingStartedDate),
        ["expected_response"] = Null(model.ExpectedResponse), ["waiting_follow_up_date"] = Null(model.WaitingFollowUpDate),
        ["waiting_escalation_action"] = Null(model.WaitingEscalationAction), ["trial_track"] = model.TrialTrack ? 1L : 0L,
        ["short_posture_summary"] = Null(model.ShortPostureSummary), ["current_issue"] = Null(model.CurrentIssue),
        ["deferred_until"] = Null(model.DeferredUntil), ["deferred_reason"] = Null(model.DeferredReason), ["deferred_at"] = Null(model.DeferredAt),
        ["deferred_by"] = Null(model.DeferredBy), ["case_status"] = model.CaseStatus, ["status_mapping_review"] = model.StatusMappingReview ? 1L : 0L,
        ["created_at"] = model.Id == 0 ? now : model.CreatedAt, ["updated_at"] = now
    };

    private static void NormalizeForSave(CaseRecord model)
    {
        if (DateOnly.TryParse(model.DateOpened, out var opened) && DateOnly.TryParse(model.ClosedDate, out var closed) && closed < opened)
            throw new ArgumentException("Date Closed cannot be before Date Opened.");
        if (string.IsNullOrWhiteSpace(model.Status)) model.Status = "Pipeline";
        if (string.IsNullOrWhiteSpace(model.Track)) model.Track = "Contested";
        if (string.IsNullOrWhiteSpace(model.MatterType)) model.MatterType = "FiledCase";
        if (string.IsNullOrWhiteSpace(model.Priority)) model.Priority = "Normal";
        if (string.IsNullOrWhiteSpace(model.CaseStatus) || model.CaseStatus == "Pipeline" && model.Status != "Pipeline")
            model.CaseStatus = MapStatus(model);
        if (string.IsNullOrWhiteSpace(model.ServiceDeadline120))
        {
            var basis = ParseDate(model.ServiceDeadlineBasisDate) ?? ParseDate(model.FilingDate);
            if (basis is not null) model.ServiceDeadline120 = basis.Value.AddDays(120).ToString("yyyy-MM-dd");
        }
    }

    private static string MapStatus(CaseRecord model)
    {
        if (model.Status == "Triage") return "Triage";
        if (model.Status is "Closed" or "Complete" || model.Stage == "Resolved") return "Resolved / Closed";
        if (model.Status == "Pipeline" || string.IsNullOrWhiteSpace(model.CaseNumber) && !string.IsNullOrWhiteSpace(model.PipelineStage)) return "Pipeline";
        if (model.Track == "Settlement") return "Settlement Pending";
        if (model.Stage == "Trial Track") return "Trial Preparation";
        if (model.Stage == "Service") return "Filed / Service Pending";
        return "Active Litigation";
    }

    private static DateOnly? ParseDate(string? value) => DateOnly.TryParse(value, out var date) && date != new DateOnly(1900, 1, 1) ? date : null;
    private static object? Null(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string JsonEscape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    private Guid? ActorUserId() =>
        httpContextAccessor.HttpContext?.Items[EntraUserProvisioningMiddleware.ProfileItemKey] is AuthenticatedUserProfile profile
            ? profile.Id
            : null;

    private const string SelectSql = """
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
               date_opened, row_version
        FROM cases
        WHERE COALESCE(is_deleted, 0) = 0
          AND (@includeClosed = 1 OR COALESCE(status,'') NOT IN ('Closed','Complete'))
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
}
