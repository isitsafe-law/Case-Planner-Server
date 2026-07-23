namespace CasePlanner.Web.Server.Models;

public sealed class CaseRecord
{
    public long Id { get; set; }
    // SQL Server optimistic-concurrency token. Null while the active runtime remains SQLite.
    public string? RowVersion { get; set; }
    public string CaseNumber { get; set; } = "";
    public string CaseName { get; set; } = "";
    public string JobNumber { get; set; } = "";
    public string Tract { get; set; } = "";
    public string County { get; set; } = "";
    public string Status { get; set; } = "";
    public string CaseStatus { get; set; } = "Pipeline";
    public bool StatusMappingReview { get; set; }
    public string Stage { get; set; } = "";
    // Contested | Settlement | Default | Friendly - drives which stage sequence
    // TrackStagePaths (App.tsx) / advance-stage logic uses for this case.
    public string Track { get; set; } = "Contested";
    public string? FilingDate { get; set; }
    public string? DateOfTaking { get; set; }
    public string? TrialDate { get; set; }
    // Optional end date for a multi-day jury trial. Purely descriptive/display - dashboard
    // triage, the attorney dashboard engine, and the deadline-template trigger all correctly
    // continue to key off TrialDate (the start date) only.
    public string? TrialEndDate { get; set; }
    public string? NextAction { get; set; }
    public string? NextActionDue { get; set; }
    public decimal? DepositAmount { get; set; }
    // Phase 5 (reporting) data-capture, zero auth/identity dependency - full dual-provider parity.
    // All three stay nullable at the DB/API level even though the Close Case client dialog treats
    // DispositionType/FinalJudgmentAmount as required at close-time - that's a client-side UX rule,
    // not a DB constraint, so existing rows and imports that predate these fields keep working.
    public decimal? FinalJudgmentAmount { get; set; }
    // "Jury Trial" | "Settlement" | "Mediation" - captured when a case closes.
    public string? DispositionType { get; set; }
    // "Partial" | "Full" | "TCE" - captured at case setup, independent of closing.
    public string? TakingType { get; set; }
    // "District 1".."District 10" (ARDOT administrative district) - fixed-dropdown like County,
    // auto-filled from County via the client's countyDistricts lookup but independently editable/
    // overridable (multiple counties share a district, so it isn't derived server-side).
    public string? District { get; set; }
    public string? Owner { get; set; }
    public string? Landowner { get; set; }
    public string? ValuationNotes { get; set; }
    public string? SettlementNotes { get; set; }
    public string? PublicationServiceNotes { get; set; }
    public bool ServiceRequired { get; set; } = true;
    public bool ServicePerfected { get; set; }
    public string? ServicePerfectedDate { get; set; }
    // Whether an answer or appearance has been filed by any defendant - the single fact this app
    // was previously missing entirely for tracking default-judgment posture on eminent-domain
    // cases (see DefaultPostureCalculator). Same shape/conventions as ServicePerfected/
    // ServicePerfectedDate immediately above: a manually-set boolean + a plain yyyy-MM-dd string,
    // never auto-inferred.
    public bool AnswerFiled { get; set; }
    public string? AnswerFiledDate { get; set; }
    // Test-build feedback batch, items 2 & 3: a plain manually-entered fact captured at case
    // closing (CloseCaseDialog), alongside DispositionType/FinalJudgmentAmount above - NOT an
    // auto-calculated/validated value. The statutory attorney's-fee-shift threshold (Ark. Code
    // Ann. Sec. 27-67-317(b)) only ever applies to a jury verdict, never a settlement, and whether
    // that threshold was actually met is already the subject of a manual-verification reminder in
    // the "Post-Trial - Core" checklist template (see TemplateSeeds) - this field intentionally
    // does not duplicate or auto-check that logic.
    public bool AttorneyFeesAwarded { get; set; }
    public decimal? AttorneyFeesAmount { get; set; }
    // Test-build feedback batch, item 5: standard Arkansas circuit-court case metadata (the
    // assigned judge and numbered division) - plain text, no dropdown/lookup (unlike
    // District/TakingType above, this isn't a fixed enumeration).
    public string? Judge { get; set; }
    public string? Division { get; set; }
    // Test-build feedback batch, item 6: additional case identifiers alongside JobNumber/Tract
    // (which remain the primary identifier pair, untouched) - FapNumber is the Federal Aid Project
    // number, ParcelNumber is the county assessor/collector's parcel identifier used when notifying
    // that office about the case action.
    public string? FapNumber { get; set; }
    public string? ParcelNumber { get; set; }
    // Test-build feedback batch, item 7: the full case caption (e.g. "State of Arkansas ex rel.
    // Arkansas State Highway Commission v. John Doe, et al.") - can be long/multi-line, captured
    // purely so it's fast to copy into documents drafted elsewhere. No document-generation
    // merge/token coupling - this repo has no existing case-field-to-document-token mechanism to
    // hook into, so this is scoped as a field + client-side copy affordance only.
    public string? CaseStyle { get; set; }
    // Test-build feedback batch, item 8: opposing counsel's phone/email/address as free text, not
    // separate structured fields - mirrors OpposingCounsel (the older plain-string field, now
    // superseded in the client UI by the OpposingAttorney child-table list) in staying a simple
    // string rather than adding new structure.
    public string? OpposingCounselContact { get; set; }
    // Test-build feedback batch, item 9: a network (UNC) path the user pastes in, e.g.
    // \\fileserver\share\JobNumber\Tract - read by POST /api/cases/{id}/open-folder to launch
    // Windows Explorer via Process.Start on the same machine the app runs on (single-machine
    // deployment model - see that endpoint's comments in Program.cs).
    public string? CaseFolderPath { get; set; }
    public string? ServiceDeadline120 { get; set; }
    public string? ServiceDeadlineBasisDate { get; set; }
    public string? ServiceMethod { get; set; }
    public string? ServiceNotes { get; set; }
    public string? ServiceStatus { get; set; }
    public string? AssignedAttorney { get; set; }
    public string? OpposingCounsel { get; set; }
    public string? Appraiser { get; set; }
    public string? TaxesOwed { get; set; }
    public string? FundsWithdrawn { get; set; }
    public string? FundsWithdrawnDate { get; set; }
    public string? DiscoveryCompleted { get; set; }
    public string? UpdatedAppraisal { get; set; }
    public string? ClosedDate { get; set; }
    public string? DateOpened { get; set; }
    public string? ProjectName { get; set; }
    public decimal? TaxOwedAmount { get; set; }
    public decimal? WholePropertyAcres { get; set; }
    public decimal? AcquisitionAcres { get; set; }
    public string? LandownerAppraiserName { get; set; }
    public string? PropertyDescription { get; set; }
    public decimal? AdditionalDepositAmount { get; set; }
    public string? AdditionalDepositDate { get; set; }
    public string? CreatedAt { get; set; }
    public string? UpdatedAt { get; set; }
    public int ChecklistTotal { get; set; }
    public int ChecklistDone { get; set; }
    public string AttentionStatus { get; set; } = "onTrack";
    // Not persisted - computed at read time from AnswerFiled/ServicePerfectedDate by
    // DefaultPostureCalculator, same treatment as AttentionStatus above (stamped alongside it in
    // CasePlannerRepository.ApplyCaseAttentionAsync and SqlServerWorkspaceQuery).
    public bool DefaultPostureWarning { get; set; }
    public string? NextDeadlineDate { get; set; }
    public string? NextDeadlineTitle { get; set; }
    // Latest of the case row's own updated_at and its checklist/deadline/discovery children's
    // updated_at - computed once in ApplyCaseAttentionAsync (same value CaseAttentionEngine's
    // "stalled" check uses) and exposed here so other consumers (e.g. DashboardTriageEngine's
    // Stale Review check) don't need to re-run that query themselves.
    public string? LastActivityAt { get; set; }

    // --- Attorney Dashboard fields (AttorneyDashboardEngine) ---
    // "PreFilingTract" | "FiledCase". A pre-filing tract is a case row that hasn't been filed yet
    // (CaseNumber is optional) and is tracked through the internal drafting/review pipeline below
    // instead of the litigation Stage field.
    public string MatterType { get; set; } = "FiledCase";
    // "Normal" | "Priority" | "Rushed".
    public string Priority { get; set; } = "Normal";
    // "Legal Assistant" | "Attorney" | "Deputy Chief Counsel" | "Chief Counsel" | "Filing Staff" | "Other".
    // Who currently has the file - independent of PipelineStage so the two can't get hard-coded together.
    public string? CurrentHolder { get; set; }
    // Pre-filing internal workflow stage - deliberately distinct from the litigation Stage field:
    // "With Legal Assistant" | "With Attorney" | "With Deputy Chief Counsel" | "With Chief Counsel" |
    // "Returned for Revision" | "Approved for Filing" | "Filed".
    public string? PipelineStage { get; set; }
    public string? DateSentToCurrentHolder { get; set; }
    public string? NextReviewDate { get; set; }
    public string? DeferredUntil { get; set; }
    public string? DeferredReason { get; set; }
    public string? DeferredAt { get; set; }
    public string? DeferredBy { get; set; }
    // Recomputed from activity_log (the meaningful-activity table), not the old MAX(updated_at)
    // proxy CaseAttentionEngine/LastActivityAt still use - see RecordActivityAsync.
    public string? LastMeaningfulActivityDate { get; set; }
    // "Moving" | "Waiting Appropriately" | "Review Required" | "Stalled".
    public string? MomentumStatus { get; set; }
    public string? WaitingReason { get; set; }
    public string? WaitingOn { get; set; }
    public string? WaitingStartedDate { get; set; }
    public string? ExpectedResponse { get; set; }
    public string? WaitingFollowUpDate { get; set; }
    public string? WaitingEscalationAction { get; set; }
    public bool TrialTrack { get; set; }
    public string? ShortPostureSummary { get; set; }
    // Pre-filing tract's current blocking issue (spec's "CurrentIssue" pipeline field) - a short
    // display string, distinct from the free-form case_notes thread.
    public string? CurrentIssue { get; set; }
}

public sealed class DiscoveryPosture
{
    public long Id { get; set; }
    public string? RowVersion { get; set; }
    public long CaseId { get; set; }
    // "No discovery currently needed" | "Written discovery first" | "Landowner deposition first" |
    // "Appraiser discovery first" | "Limited targeted discovery" | "Full discovery plan" |
    // "Awaiting owner appraisal before deciding" | "Strategy deferred until a stated event" |
    // "Strategy not selected".
    public string Strategy { get; set; } = "Strategy not selected";
    public string? StrategyReason { get; set; }
    public string? StrategySelectedDate { get; set; }
    public string? DiscoveryServedDate { get; set; }
    public string? ResponsesDueDate { get; set; }
    public string? ResponsesReceivedDate { get; set; }
    public string? ResponsesReviewedDate { get; set; }
    public string? DiscoveryCutoffDate { get; set; }
    public string? PlannedDepositions { get; set; }
    public string? DeficiencyStatus { get; set; }
    public string? NextDecision { get; set; }
    public string? NextReviewDate { get; set; }
    public bool IsComplete { get; set; }
    public string? CompletionChangedAt { get; set; }
    public string? CompletionChangedBy { get; set; }
    public string? CreatedAt { get; set; }
    public string? UpdatedAt { get; set; }
}

public sealed class PipelineHandoffRecord
{
    public long Id { get; set; }
    public string? RowVersion { get; set; }
    public long CaseId { get; set; }
    public string? PreviousHolder { get; set; }
    public string NewHolder { get; set; } = "";
    public string? PreviousStage { get; set; }
    public string NewStage { get; set; } = "";
    public string? HandoffDate { get; set; }
    public string? NextReviewDate { get; set; }
    public string? Note { get; set; }
    public string? CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? CaseRowVersion { get; set; }
}

public sealed class ActivityLogEntry
{
    public long Id { get; set; }
    public long CaseId { get; set; }
    // One of the qualifying ActivityType values (see AttorneyDashboardEngine.MeaningfulActivityTypes)
    // or "Other" for a routine/manual note that should not reset the momentum clock.
    public string ActivityType { get; set; } = "Other";
    public bool IsMeaningful { get; set; } = true;
    public string OccurredAt { get; set; } = "";
    public string? Notes { get; set; }
    public string? CreatedAt { get; set; }
    public string? ActorUserId { get; set; }
    public string? ActorDisplay { get; set; }
    public string? RowVersion { get; set; }
    // Edit history (original value / new value / reason, no silent overwrite) - same pattern as
    // DeadlineItem.History. Empty for never-edited entries.
    public List<ActivityLogHistoryEntry> History { get; set; } = [];
}

// One prior version of an edited activity entry, including the authenticated editor.
public sealed class ActivityLogHistoryEntry
{
    public long Id { get; set; }
    public long ActivityId { get; set; }
    public string? PreviousType { get; set; }
    public string? NewType { get; set; }
    public string? PreviousOccurredAt { get; set; }
    public string? NewOccurredAt { get; set; }
    public string? PreviousNotes { get; set; }
    public string? NewNotes { get; set; }
    public string? Reason { get; set; }
    public string? CreatedAt { get; set; }
    public string? EditedByUserId { get; set; }
    public string? EditedByDisplay { get; set; }
}

public sealed class UpdateActivityRequest
{
    public string? RowVersion { get; set; }
    public string ActivityType { get; set; } = "Other";
    public string OccurredAt { get; set; } = "";
    public string? Notes { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class DeadlineItem
{
    public long Id { get; set; }
    public string? RowVersion { get; set; }
    public long CaseId { get; set; }
    public string Title { get; set; } = "";
    public string? DueDate { get; set; }
    public string Status { get; set; } = "Open";
    public string? Notes { get; set; }
    public string SourceType { get; set; } = "Manual";
    public string SourceKind { get; set; } = "Manual";
    public string? SourceTemplateId { get; set; }
    public int? SourceTemplateVersion { get; set; }
    public string? SourceStage { get; set; }
    public string? GeneratedAt { get; set; }
    public string? GeneratedBy { get; set; }
    public bool IsManual { get; set; } = true;
    public string Severity { get; set; } = "normal";
    public string? CompletedAt { get; set; }
    public List<DeadlineHistoryEntry> History { get; set; } = [];
    // Transient: optional reason captured on save when DueDate changes for an existing deadline. Never persisted to the deadlines row itself.
    public string? ReasonForChange { get; set; }
}

public sealed class DeadlineHistoryEntry
{
    public string? PreviousDueDate { get; set; }
    public string? NewDueDate { get; set; }
    public string? Reason { get; set; }
    public string ChangedAt { get; set; } = "";
}

public sealed class ChecklistItemRecord
{
    public long Id { get; set; }
    public string? RowVersion { get; set; }
    public long CaseId { get; set; }
    public string Phase { get; set; } = "";
    public string Task { get; set; } = "";
    public string? DueDate { get; set; }
    public string Status { get; set; } = "Not Started";
    public string? Notes { get; set; }
    public string SourceType { get; set; } = "Manual";
    public string SourceKind { get; set; } = "Manual";
    public string? SourceTemplateId { get; set; }
    public int? SourceTemplateVersion { get; set; }
    public string? SourceStage { get; set; }
    public string? GeneratedAt { get; set; }
    public string? GeneratedBy { get; set; }
    public bool IsManual { get; set; } = true;
    public string? CompletedAt { get; set; }
    // Item 2 (multi-user rollout Phase 2): a legal assistant this task is assigned to. Nullable and
    // opaque on SQLite (no app_users table there to validate against - just a passthrough GUID
    // string so the column round-trips structurally on both providers); only meaningfully
    // populated/selectable when Entra is enabled and a real app_users/case_assignments roster
    // exists (SQL Server pilot schema), same "inert locally" pattern as Phase 1's roster UI.
    public string? AssignedUserId { get; set; }
    // Test-build feedback batch (task assignment): a plain name snapshot, not a foreign key -
    // same "opaque name" convention as cases.assigned_attorney / case_legal_assistants.name. This
    // is the field the UI actually reads/writes today (assignable from the case's Assigned
    // Attorney plus its Legal Assistants); AssignedUserId above stays fully dormant, reserved for
    // a future real Entra deployment, and is intentionally left untouched by this field.
    public string? AssignedStaffName { get; set; }
}

// Multi-user rollout Phase 4a (notifications core). recipient_user_id follows the same opaque
// passthrough convention as checklist_items.assigned_user_id above (TEXT on SQLite, uniqueidentifier
// FK'd to dbo.app_users on SQL Server). NotificationType is a plain validated string (TaskAssigned/
// TaskCompleted today; DeadlineReminder is Phase 4b) rather than a DB enum, matching case_role/
// assignment_role/checklist status.
public sealed class NotificationRecord
{
    public long Id { get; set; }
    public string RecipientUserId { get; set; } = "";
    public long? CaseId { get; set; }
    public string NotificationType { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Body { get; set; }
    public bool IsRead { get; set; }
    public string CreatedAt { get; set; } = "";
    public string? ReadAt { get; set; }
}

public sealed class NotificationFeed
{
    public List<NotificationRecord> Items { get; set; } = [];
    public int UnreadCount { get; set; }
}

// Multi-user rollout Phase 4c (per-user notification preferences). UserId follows
// NotificationRecord.RecipientUserId's opaque passthrough convention above. All six booleans default
// true (enabled) so a user with no saved row - the common case, since a row is only ever written once
// someone changes something - reads back as "everything on" rather than "everything off".
public sealed class NotificationPreferencesRecord
{
    public string UserId { get; set; } = "";
    public bool TaskAssignedInApp { get; set; } = true;
    public bool TaskAssignedEmail { get; set; } = true;
    public bool TaskCompletedInApp { get; set; } = true;
    public bool TaskCompletedEmail { get; set; } = true;
    public bool DeadlineReminderInApp { get; set; } = true;
    public bool DeadlineReminderEmail { get; set; } = true;
}

// The PUT /api/notification-preferences request body - deliberately has no UserId field, since the
// preferences being saved are always the current actor's own (resolved server-side from
// IApplicationActorContext), never an arbitrary target user.
public sealed record NotificationPreferencesUpdateRequest(
    bool TaskAssignedInApp,
    bool TaskAssignedEmail,
    bool TaskCompletedInApp,
    bool TaskCompletedEmail,
    bool DeadlineReminderInApp,
    bool DeadlineReminderEmail);

public sealed class DiscoveryItemRecord
{
    public long Id { get; set; }
    public string? RowVersion { get; set; }
    public long CaseId { get; set; }
    public string? RequestTitle { get; set; }
    public string Direction { get; set; } = "Served by Us";
    public string DiscoveryType { get; set; } = "Interrogatories";
    public string? ServedDate { get; set; }
    public string? DueDate { get; set; }
    public string? ResponseDate { get; set; }
    public string? FollowUpDate { get; set; }
    public string Status { get; set; } = "Waiting for Responses";
    public string? AssignedTo { get; set; }
    public string? Notes { get; set; }
    public string? EscalationNote { get; set; }
    public string? GoodFaithSentDate { get; set; }
    public string? MotionToCompelDate { get; set; }
}

// Pure, unit-testable discovery due-date default: a case-level deadline template can't represent
// this because a case has many discovery requests, each with its own ServedDate/DueDate (unlike
// filing_date/trial_date, which are single-value trigger fields on the case row) - see
// DeadlineTemplateRecord's TriggerField. Never overwrites a value already present, whether that's
// a manually-typed override or a previously-computed default from an earlier save.
public static class DiscoveryDueDateCalculator
{
    public const int DefaultResponseWindowDays = 30;

    public static string? ComputeDefaultDueDate(string? servedDate, string? currentDueDate)
    {
        if (!string.IsNullOrWhiteSpace(currentDueDate))
        {
            return currentDueDate;
        }

        if (string.IsNullOrWhiteSpace(servedDate) || !DateOnly.TryParse(servedDate, out var served))
        {
            return currentDueDate;
        }

        return served.AddDays(DefaultResponseWindowDays).ToString("yyyy-MM-dd");
    }
}

public sealed class PublicationEntryRecord
{
    public long Id { get; set; }
    public string? RowVersion { get; set; }
    public long CaseId { get; set; }
    public string PublicationNumber { get; set; } = "";
    public string? PublicationDate { get; set; }
    public string? Newspaper { get; set; }
    public bool ProofFiled { get; set; }
    public string? ProofFiledDate { get; set; }
    public bool ServiceResolved { get; set; }
    public string? Notes { get; set; }
}

// Item 2 (multi-user rollout Phase 2): case.opposingCounsel was a single free-text string with no
// document-generation coupling anywhere - pure case-record display. Converted to a one-to-many
// child table (case_opposing_attorneys) mirroring the simple per-case list pattern used by
// witnesses/publication_dates. The old opposing_counsel column on cases is kept (not dropped) and
// a one-time startup migration copies any existing non-blank value into a first row here.
public sealed class OpposingAttorneyRecord
{
    public long Id { get; set; }
    public string? RowVersion { get; set; }
    public long CaseId { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
}

// Test-build feedback item: case.assignedAttorney's tied legal assistant used to be derived live
// (whichever Staff Directory legal assistant lists this case's attorney) and shown as a single
// read-only value - unable to represent two attorneys on one case each needing their own legal
// assistant, or a manual override. Converted to a one-to-many child table (case_legal_assistants)
// mirroring OpposingAttorneyRecord/case_opposing_attorneys column-for-column. Unlike opposing
// attorneys, Name is chosen client-side from the Staff Directory's active legal assistants via a
// dropdown rather than typed as free text, but is still stored as a plain name snapshot (not a
// foreign key) so a name later deactivated/removed from the directory stays intact here.
public sealed class CaseLegalAssistantRecord
{
    public long Id { get; set; }
    public string? RowVersion { get; set; }
    public long CaseId { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
}

// Multiple defendants (often heirs to a property) can answer at genuinely different times - one
// attorney appearing, or an answer being filed at all, typically signals the primary landowner
// engaging on just compensation, while a distant heir who never responds shouldn't necessarily
// read as the whole case being in default. The prior case-level AnswerFiled/AnswerFiledDate pair
// (a single global boolean) could not represent that. CaseDefendantRecord is a real one-to-many
// child table (case_defendants) mirroring CaseLegalAssistantRecord/case_legal_assistants's shape,
// with additional per-defendant fields needed to track service and answer status individually.
// Address is deliberately kept as one free-text field rather than a nested one-to-many address
// sub-table - a defendant occasionally has more than one address, or a mix of an address and a
// note (e.g. "186 Carr Court, Harrison, AR 72601, and P.O. Box 21, Western Grove, AR 72685"), and
// that is fine to represent as plain text. ServiceMethod must support "Warning Order" as a real
// value since Unknown Heirs entries are served that way with no address - see
// DefaultPostureCalculator.IsLikelyDefaultForDefendants, which treats a Warning-Order-only entry
// with no address as not "actually served" for default-posture purposes.
public sealed class CaseDefendantRecord
{
    public long Id { get; set; }
    public string? RowVersion { get; set; }
    public long CaseId { get; set; }
    public int SortOrder { get; set; }
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string? ServiceMethod { get; set; }
    public string? ServedDate { get; set; }
    public bool AnswerFiled { get; set; }
    public string? AnswerFiledDate { get; set; }
    public string? Notes { get; set; }
}

// Pre-suit intake gate (pipeline-phase chain: Legal Assistant -> Attorney -> Deputy Chief Counsel
// -> Chief Counsel): an append-only per-holder review log. Every Approve/Return-for-Revision
// action is a NEW row, never an update to an existing one, so history survives a cycle like
// Approved -> Returned -> re-Approved. "Pending" is never itself stored as a row - the absence of
// any row yet for a given (CaseId, HolderRole) pair already means pending. See
// PipelinePromotionGate (CasePlanner.Web.Server.Persistence) for how this log gates forward
// advancement of CaseRecord.CurrentHolder.
public sealed class PipelineHolderApprovalRecord
{
    public long Id { get; set; }
    public long CaseId { get; set; }
    // "Legal Assistant" | "Attorney" | "Deputy Chief Counsel" | "Chief Counsel" - matches
    // CaseRecord.CurrentHolder's spellings exactly (see PipelinePromotionGate.GatedChain).
    public string HolderRole { get; set; } = "";
    // "Approved" | "Returned".
    public string Status { get; set; } = "";
    public string? Note { get; set; }
    public string SetAt { get; set; } = "";
    // Free text, no real authentication yet to bind this to (Entra ID is dormant) - same "records
    // whoever the client says acted, with no cryptographic proof" limitation
    // PipelineHandoffRecord.CreatedBy already carries. This enforces PROCESS (you can't advance
    // without clicking Approve), not IDENTITY.
    public string? SetByDisplayName { get; set; }
}

// Client-facing shape for POST /api/cases/{id}/pipeline-approvals (Task C's Approve / Return for
// Revision action) - distinct from PipelineHolderApprovalRecord itself because this also carries
// the orchestration inputs (which holder role, which direction) rather than just the log fields.
public sealed class RecordPipelineHolderApprovalRequest
{
    public string HolderRole { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Note { get; set; }
    public string? SetByDisplayName { get; set; }
}

public sealed class UpcomingWorkItemRecord
{
    public string Key { get; set; } = "";
    public long CaseId { get; set; }
    public string CaseName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Type { get; set; } = "";
    public string? DueDate { get; set; }
    public string Urgency { get; set; } = "No Due Date";
    public bool IsOverdue { get; set; }
    public string Tab { get; set; } = "overview";
}

public sealed class ReportExcelRequest
{
    public string FileName { get; set; } = "Case_Report.xlsx";
    public string Title { get; set; } = "Case Report";
    public string GeneratedAt { get; set; } = "";
    public Dictionary<string, string> Filters { get; set; } = [];
    public List<ReportExcelColumn> Columns { get; set; } = [];
    public List<Dictionary<string, string>> Rows { get; set; } = [];
}

public sealed class ReportExcelColumn
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
}

public sealed class PublicationRecord
{
    public long CaseId { get; set; }
    public string? RowVersion { get; set; }
    public string? FirstPublicationDate { get; set; }
    public string? SecondPublicationDate { get; set; }
    public string? PublicationName { get; set; }
    public bool MarkedPerfected { get; set; }
    public string? LastUpdatedAt { get; set; }
    public string? LastUpdatedBy { get; set; }
    // Allows a date to be saved without a publication name after the UI has shown a warning.
    public bool OverrideMissingPublicationName { get; set; }
}

public sealed class IssueTagRecord
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Category { get; set; }
}

public sealed class CaseIssueTagRecord
{
    public long Id { get; set; }
    public long CaseId { get; set; }
    public long IssueTagId { get; set; }
    public string TagName { get; set; } = "";
    public string? Category { get; set; }
    public string? Description { get; set; }
    public string? Notes { get; set; }
    public string? RowVersion { get; set; }
}

public sealed class DocumentExportRecord
{
    public long Id { get; set; }
    public long CaseId { get; set; }
    public string DocumentType { get; set; } = "";
    public string DocumentTitle { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string Status { get; set; } = "Generated";
    public string QaStatus { get; set; } = "Not Reviewed";
    public string? QaNotes { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ContentText { get; set; }
    public string? BaseTemplateVersion { get; set; }
    public string? IssueTagVersions { get; set; }
    public string? MergeFieldValues { get; set; }
    public bool IsDraft { get; set; }
    public bool IsFinalized { get; set; }
    public string? CreatedByUserId { get; set; }
    public string? CreatedByDisplay { get; set; }
    public string? QaReviewedByUserId { get; set; }
    public string? QaReviewedByDisplay { get; set; }
    public string? RowVersion { get; set; }
}

public sealed class CaseNoteRecord
{
    public long Id { get; set; }
    public long CaseId { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public string? RowVersion { get; set; }
}

public sealed class HearingRecord
{
    public long Id { get; set; }
    public long CaseId { get; set; }
    public string Title { get; set; } = "";
    // Generalizes the old "Hearings" tab into "Events" - Hearing/Deposition/Mediation/Filing
    // Deadline/Other, or a free-typed custom label. Existing rows default to "Hearing" (see
    // migration 028_case_events.sql); the hearings table/HearingRecord name are unchanged to
    // avoid a much larger mechanical rename across the dashboard/work-queue/SQL-pilot code that
    // reads this table - only the client-facing tab label and event-type vocabulary changed.
    public string EventType { get; set; } = "Hearing";
    // Scheduled | Completed | Continued | Canceled (see migration 029). Existing rows default
    // to "Scheduled".
    public string Status { get; set; } = "Scheduled";
    public string? HearingDate { get; set; }
    public string? Location { get; set; }
    public string? Description { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public string? RowVersion { get; set; }
}

public sealed class FileExportResult
{
    public string Title { get; set; } = "";
    public string OutputPath { get; set; } = "";
}

public sealed class ChecklistTemplateItemRecord
{
    public long Id { get; set; }
    public string? RowVersion { get; set; }
    public long TemplateId { get; set; }
    public string Task { get; set; } = "";
    public string? Phase { get; set; }
    public int SortOrder { get; set; }
    public int? DueOffsetDays { get; set; }
}

public sealed class ChecklistTemplateRecord
{
    public long Id { get; set; }
    public string? RowVersion { get; set; }
    public string Name { get; set; } = "";
    public string TriggerType { get; set; } = "Stage";
    public string? Stage { get; set; }
    public string? IssueTagName { get; set; }
    // 0/false = stock seed content, safe to refresh on a version-bump reseed. 1/true = a firm has
    // touched this template (created, edited, or edited/deleted one of its items) through the
    // Template Editor - permanently off-limits to reseeding from then on. See
    // SeedChecklistTemplatesAsync/SaveChecklistTemplateAsync.
    public bool IsCustom { get; set; }
    public bool Active { get; set; } = true;
    public List<ChecklistTemplateItemRecord> Items { get; set; } = [];
}

public sealed class BackupInfo
{
    public string FileName { get; set; } = "";
    public long SizeBytes { get; set; }
    public string CreatedAt { get; set; } = "";
}

public sealed class RestoreBackupRequest
{
    public string FileName { get; set; } = "";
}

public sealed class DatabaseResetRequest
{
    public string Scope { get; set; } = "database";
    public string Confirmation { get; set; } = "";
}

public sealed class DashboardData
{
    public int OverdueDeadlines { get; set; }
    public int DueIn7Days { get; set; }
    public int DueIn30Days { get; set; }
    public int UpcomingTrials { get; set; }
    public int DiscoveryDue { get; set; }
    public int DiscoveryFollowUps { get; set; }
    public int ChecklistDueSoon { get; set; }
    public int ServiceDueSoon { get; set; }
    public int ServiceOverdue { get; set; }
    public int CasesWithoutPerfectedService { get; set; }
    public int MissingServiceDeadline { get; set; }
    public int CasesNeedingReview { get; set; }
    public int ActiveCaseCount { get; set; }
    public int CasesUrgentCount { get; set; }
    public int CasesAttentionCount { get; set; }
    public int CasesUnconfirmedCount { get; set; }
    public int CasesStalledCount { get; set; }
    public int CasesOnTrackCount { get; set; }
    public List<CaseRecord> AttentionCases { get; set; } = [];
    public List<AttentionItem> TodaysAgenda { get; set; } = [];
    public List<AttentionItem> UpcomingDates { get; set; } = [];
    public List<DashboardTriageEntry> TriageQueue { get; set; } = [];
    public int NeedsActionNowCount { get; set; }
    public int ServiceRiskCount { get; set; }
    public int HardDeadlinesSoonCount { get; set; }
    public int CourtEventsSoonCount { get; set; }
    public int BlockedCount { get; set; }
    public int StaleReviewCount { get; set; }
}

public sealed class AttorneyDashboardFilters
{
    public string? MatterType { get; set; }
    public string? Project { get; set; }
    public string? County { get; set; }
    public string? Priority { get; set; }
    public string? CurrentHolder { get; set; }
    public string? Stage { get; set; }
    public bool? TrialTrack { get; set; }
    public string? MomentumStatus { get; set; }
    public string? Search { get; set; }
}

// Top-level response for GET /api/dashboard/attorney, computed by AttorneyDashboardEngine.
// One aggregation call, not a per-section round trip - see GetAttorneyDashboardAsync.
public sealed class AttorneyDashboardResponse
{
    public AttorneyDashboardSummaryCounts SummaryCounts { get; set; } = new();
    public List<ActionQueueItem> ActionQueue { get; set; } = [];
    public DiscoveryControlSummary DiscoveryControl { get; set; } = new();
    public List<MomentumReviewEntry> MomentumReview { get; set; } = [];
    public FilingPipelineView FilingPipeline { get; set; } = new();
    public List<TrialWatchEntry> TrialWatch { get; set; } = [];
    public List<UpcomingDecisionItem> UpcomingDecisions { get; set; } = [];
    public List<ProjectWatchRow> ProjectWatch { get; set; } = [];
    public AttorneyDocketSummary DocketSummary { get; set; } = new();
    // Imported cases still waiting on the triage wizard - excluded from every panel above,
    // surfaced only as the "N cases awaiting triage" banner.
    public int TriageCaseCount { get; set; }
}

// The 6 top summary-filter cards. Deliberately no decorative totals (total cases, total notes,
// etc.) per the dashboard brief - every count here corresponds to a real filter a card applies.
public sealed class AttorneyDashboardSummaryCounts
{
    public int NeedsJudgment { get; set; }
    public int Stalled { get; set; }
    public int DiscoveryUnset { get; set; }
    public int OnMyDesk { get; set; }
    public int TrialTrack { get; set; }
    public int MissingNextReview { get; set; }
}

// One consolidated Attorney Action Queue row - one entry per case even if multiple warnings
// apply (RelatedWarningCount surfaces "and N more"), per the brief's consolidation rule.
public sealed class ActionQueueItem
{
    public long CaseId { get; set; }
    public string CaseName { get; set; } = "";
    public string? CaseNumber { get; set; }
    public string? JobNumber { get; set; }
    public string CurrentPhase { get; set; } = "";
    // "Decide" | "Act" | "Review" | "Escalate" | "Prepare".
    public string ActionCategory { get; set; } = "";
    // 1 (Immediate) - 4 (Planned Work). Lower is more urgent.
    public int PriorityLevel { get; set; }
    public string Reason { get; set; } = "";
    public string PostureSummary { get; set; } = "";
    public string RecommendedNextAction { get; set; } = "";
    public string? ReviewDate { get; set; }
    public int? DaysSinceMeaningfulActivity { get; set; }
    public int RelatedWarningCount { get; set; }
    public string? CurrentHolder { get; set; }
    public string MatterType { get; set; } = "FiledCase";
    public long? RelatedDeadlineId { get; set; }
}

public sealed class DiscoveryControlSummary
{
    public int StrategyNotSelected { get; set; }
    public int StrategySelectedNotServed { get; set; }
    public int ResponsesOverdue { get; set; }
    public int ResponsesReceivedNotReviewed { get; set; }
    public int DeficienciesUnresolved { get; set; }
    public int DepositionDecisionPending { get; set; }
    public int CutoffApproaching { get; set; }
    public int Complete { get; set; }
    public int NoDiscoveryNeeded { get; set; }
    // Cases keyed by which of the 9 conditions above they currently match, for the panel's
    // clickable filtered views - a case can appear under more than one condition.
    public Dictionary<string, List<DiscoveryControlCaseRef>> CasesByCondition { get; set; } = [];
}

public sealed class DiscoveryControlCaseRef
{
    public long CaseId { get; set; }
    public string CaseName { get; set; } = "";
    public string? CaseNumber { get; set; }
    public string Strategy { get; set; } = "";
    public string? NextDecision { get; set; }
    public string? NextReviewDate { get; set; }
}

public sealed class MomentumReviewEntry
{
    public long CaseId { get; set; }
    public string CaseName { get; set; } = "";
    public string? CaseNumber { get; set; }
    // "Moving" | "Waiting Appropriately" | "Review Required" | "Stalled".
    public string MomentumStatus { get; set; } = "";
    public int DaysSinceMeaningfulActivity { get; set; }
    public string? WaitingOn { get; set; }
    public string? WaitingFollowUpDate { get; set; }
}

public sealed class FilingPipelineView
{
    public List<PreFilingTractRow> MyDesk { get; set; } = [];
    public List<PreFilingTractRow> Waiting { get; set; } = [];
    public List<PreFilingTractRow> AllPipeline { get; set; } = [];
}

public sealed class PreFilingTractRow
{
    public long CaseId { get; set; }
    public string TractOrOwnerName { get; set; } = "";
    public string? ProjectName { get; set; }
    public string? JobNumber { get; set; }
    public string? County { get; set; }
    public string? CurrentHolder { get; set; }
    public string? PipelineStage { get; set; }
    public string? DateSentToCurrentHolder { get; set; }
    public string Priority { get; set; } = "Normal";
    public string? NextReviewDate { get; set; }
    public string? CurrentIssue { get; set; }
    public string? LastFollowUpDate { get; set; }
    public string? LastUpdated { get; set; }
    // Why this row appears in My Desk / needs monitoring while Waiting - null for a plain
    // "waiting, nothing to see" row.
    public string? FlagReason { get; set; }
}

public sealed class TrialWatchEntry
{
    public long CaseId { get; set; }
    public string CaseName { get; set; } = "";
    public string? CaseNumber { get; set; }
    public string? TrialDate { get; set; }
    public int? DaysUntilTrial { get; set; }
    public decimal? Deposit { get; set; }
    public decimal? StateAppraisal { get; set; }
    public decimal? OwnerAppraisal { get; set; }
    public decimal? OwnerDemand { get; set; }
    public decimal? LastOffer { get; set; }
    public decimal? SettlementAuthority { get; set; }
    // Present only when the statutory fee-comparison point is actually in play for this case
    // (trial-watch eligible per AttorneyDashboardEngine.IsTrialWatchEligible) - never a generic
    // valuation-gap warning. See the neutral wording rule in the dashboard brief.
    public string? FeeComparisonNote { get; set; }
    public string DiscoveryStatus { get; set; } = "";
    public string? WitnessReadiness { get; set; }
    public string? ExhibitReadiness { get; set; }
    public string? NextTrialDecision { get; set; }
}

public sealed class UpcomingDecisionItem
{
    public long CaseId { get; set; }
    public string CaseName { get; set; } = "";
    public string DecisionType { get; set; } = "";
    public string? RelevantDate { get; set; }
    public string? Context { get; set; }
    public string? RecommendedPreparationDate { get; set; }
    public string Status { get; set; } = "Pending";
}

public sealed class ProjectWatchRow
{
    public string ProjectName { get; set; } = "";
    public string? JobNumber { get; set; }
    public int TractCount { get; set; }
    public int PreFilingCount { get; set; }
    public int FiledCount { get; set; }
    public int ResolvedCount { get; set; }
    public int OnAttorneyDeskCount { get; set; }
    public int StalledCount { get; set; }
    public string? EarliestTrialDate { get; set; }
    public string? OldestInactiveMatter { get; set; }
    // Only populated when a genuine shared issue is detected (repeated valuation theory, same
    // appraiser delay across tracts, etc.) - never created merely because tracts share a job
    // number, per the brief's explicit "do not create project warnings merely because..." rule.
    public string? SharedIssue { get; set; }
    public string? NextProjectDecision { get; set; }
}

public sealed class AttorneyDocketSummary
{
    public int PreFilingMatters { get; set; }
    public int FiledMatters { get; set; }
    public int TrialTrackMatters { get; set; }
    public int WaitingAppropriately { get; set; }
    public int OnAttorneysDesk { get; set; }
    public int MissingNextReviewDate { get; set; }
}

// --- Quick-action request shapes ---

public sealed class SetNextActionRequest
{
    public string? RowVersion { get; set; }
    public string? NextAction { get; set; }
    public string? NextReviewDate { get; set; }
}

public sealed class SetWaitingRequest
{
    public string? RowVersion { get; set; }
    public string WaitingOn { get; set; } = "";
    public string? WaitingReason { get; set; }
    public string? WaitingStartedDate { get; set; }
    public string? ExpectedResponse { get; set; }
    public string WaitingFollowUpDate { get; set; } = "";
    public string? WaitingEscalationAction { get; set; }
}

public sealed class DeferActionRequest
{
    public string? RowVersion { get; set; }
    public string Reason { get; set; } = "";
    public string FutureReviewDate { get; set; } = "";
}

public sealed class DeadlineTemplateRecord
{
    public long Id { get; set; }
    public string? RowVersion { get; set; }
    public string Name { get; set; } = "";
    public string TriggerField { get; set; } = "filing_date";
    public int OffsetDays { get; set; }
    public string Title { get; set; } = "";
    public string Severity { get; set; } = "normal";
    // See ChecklistTemplateRecord.IsCustom - identical ownership rule for deadline templates.
    public bool IsCustom { get; set; }
    public bool Active { get; set; } = true;
}
// Staff Directory - multi-user rollout Phase 5 (reporting) prerequisite. A fixed list of real
// attorney/legal-assistant names for case metadata and reporting, deliberately separate from the
// dormant Entra-provisioned app_users roster (AuthenticatedUserProfile/AppUserSummary above): this
// has zero auth/identity dependency and works fully on SQLite today, like County/District.
public sealed class AttorneyRecord
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Title { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    // Manual link to the real Entra-login account this Staff Directory row corresponds to - see
    // 042_staff_directory_linked_user.sql. A GUID string on SQLite (opaque passthrough, no
    // app_users table there to validate against); a real uniqueidentifier FK on SQL Server.
    public string? LinkedUserId { get; set; }
}

public sealed class LegalAssistantRecord
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public List<long> AttorneyIds { get; set; } = [];
    public List<string> AttorneyNames { get; set; } = [];
    public string? LinkedUserId { get; set; }
}

public sealed class WorkTemplateCandidate
{
    public string Kind { get; set; } = "Task";
    public string TemplateId { get; set; } = "";
    public int TemplateVersion { get; set; } = 1;
    public string Title { get; set; } = "";
    public string Stage { get; set; } = "";
    public string? DueDate { get; set; }
    public string? Severity { get; set; }
    public bool IsDuplicate { get; set; }
    public string? DuplicateReason { get; set; }
}
public sealed class AddWorkTemplateSelection
{
    public string Kind { get; set; } = "Task";
    public string TemplateId { get; set; } = "";
    public string? DueDate { get; set; }
    public bool AllowDuplicate { get; set; }
}
public sealed class AddWorkTemplatesRequest { public List<AddWorkTemplateSelection> Items { get; set; } = []; }

public sealed class DiscoveryTemplateItemRecord
{
    public long Id { get; set; }
    public string? RowVersion { get; set; }
    public string StableKey { get; set; } = "";
    public int Version { get; set; } = 1;
    public string Category { get; set; } = "BaseInterrogatory";
    public string? IssueTagName { get; set; }
    public string Track { get; set; } = "Any";
    public string Wording { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }
    public bool IsDefault { get; set; }
    public string? CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
}

public sealed class DiscoveryTemplatePreview
{
    public long CaseId { get; set; }
    public string RenderedText { get; set; } = "";
    public List<string> IssueTags { get; set; } = [];
    public List<string> TemplateVersions { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class DiscoveryBaseVersionRecord
{
    public long Id { get; set; }
    public string? RowVersion { get; set; }
    public string DocumentType { get; set; } = "interrogatories";
    public int Version { get; set; }
    public string Content { get; set; } = "";
    public bool Active { get; set; } = true;
    public string CreatedAt { get; set; } = "";
    public string? CreatedBy { get; set; }
}

public sealed class DiscoveryTemplateValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Issues { get; set; } = [];
}

public sealed class ClearDefermentRequest
{
    public string? Reason { get; set; }
}

public sealed class BulkDeferActionRequest
{
    public List<long> CaseIds { get; set; } = [];
    public Dictionary<long, string> RowVersions { get; set; } = [];
    public string Reason { get; set; } = "";
    public string FutureReviewDate { get; set; } = "";
}

public sealed class SetHolderRequest
{
    public string? RowVersion { get; set; }
    public string CurrentHolder { get; set; } = "";
}

public sealed class SetPriorityRequest
{
    public string? RowVersion { get; set; }
    public string Priority { get; set; } = "Normal";
}

public sealed class SetTrialTrackRequest
{
    public string? RowVersion { get; set; }
    public bool TrialTrack { get; set; }
}

public sealed class SetDiscoveryStrategyRequest
{
    public string Strategy { get; set; } = "";
    public string? StrategyReason { get; set; }
}

public sealed class RecordActivityRequest
{
    public string ActivityType { get; set; } = "Other";
    public string? Notes { get; set; }
    public string? OccurredAt { get; set; }
}

public sealed class ShortNoteRequest
{
    public string? RowVersion { get; set; }
    public string Note { get; set; } = "";
}

public sealed class PipelineHandoffRequest
{
    public string? RowVersion { get; set; }
    public string NewHolder { get; set; } = "";
    public string NewStage { get; set; } = "";
    public string? HandoffDate { get; set; }
    public string? NextReviewDate { get; set; }
    public string? Note { get; set; }
}

// One Work Queue row on the redesigned Dashboard: why this active case needs attention right
// now, computed by DashboardTriageEngine. "Blocked" is a proxy today (an overdue discovery
// follow-up waiting on the other side) - there's no general case-level "blocked on an outside
// party" flag in the data model yet, so it only catches that one concrete situation.
public sealed class DashboardTriageEntry
{
    public long CaseId { get; set; }
    public string CaseName { get; set; } = "";
    public string CaseNumber { get; set; } = "";
    public string Category { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Timing { get; set; } = "";
    public string? Stage { get; set; }
    public string? Track { get; set; }
    public string NextAction { get; set; } = "";
    public string? DueDate { get; set; }
    public int PriorityScore { get; set; }
    public List<string> MatchedCategories { get; set; } = [];
}

public sealed class ServiceStatusSummary
{
    public bool ServiceRequired { get; set; } = true;
    public bool ServicePerfected { get; set; }
    public string? ServicePerfectedDate { get; set; }
    public string? ServiceDeadline120 { get; set; }
    public string? ServiceDeadlineBasisDate { get; set; }
    public string? ServiceMethod { get; set; }
    public string? ServiceStatus { get; set; }
    public string? ServiceNotes { get; set; }
    public string WarningLevel { get; set; } = "none";
    public string WarningText { get; set; } = "";
    public int? DaysRemaining { get; set; }
    public bool ServiceDeadlineCalculated { get; set; }
    public string? PublicationDate { get; set; }
    public string? Newspaper { get; set; }
    public string? ProofFiledDate { get; set; }
    public bool PublicationEntryExists { get; set; }
    public string? PublicationNotes { get; set; }
}

public sealed class ServiceQueueItem
{
    public long CaseId { get; set; }
    public string CaseName { get; set; } = "";
    public string CaseNumber { get; set; } = "";
    public string JobNumber { get; set; } = "";
    public string Tract { get; set; } = "";
    public string County { get; set; } = "";
    public string? FilingDate { get; set; }
    public string? ServiceDeadlineBasisDate { get; set; }
    public string? ServiceDeadline120 { get; set; }
    public int? DaysRemaining { get; set; }
    public bool ServiceRequired { get; set; } = true;
    public bool ServicePerfected { get; set; }
    public string? ServicePerfectedDate { get; set; }
    public string? ServiceMethod { get; set; }
    public string? ServiceStatus { get; set; }
    public string? NotesPreview { get; set; }
    public string WarningLevel { get; set; } = "none";
    public string WarningText { get; set; } = "";
}

public sealed class AttentionItem
{
    public string Kind { get; set; } = "";
    public long CaseId { get; set; }
    public long? ItemId { get; set; }
    public string CaseName { get; set; } = "";
    public string CaseNumber { get; set; } = "";
    public string Summary { get; set; } = "";
    public string? DueDate { get; set; }
    public string TargetTab { get; set; } = "overview";
}

public sealed class DiagnosticsSnapshot
{
    public string AppName { get; set; } = "";
    public string Version { get; set; } = "";
    public string DatabaseProvider { get; set; } = "SQLite (active runtime)";
    public string DatabaseArchitectureNote { get; set; } = "";
    public string DatabasePath { get; set; } = "";
    public bool DatabaseWritable { get; set; }
    public bool BackupsWritable { get; set; }
    public bool ExportsWritable { get; set; }
    public bool LogsWritable { get; set; }
    public bool WriteSafetyOk { get; set; }
    public string WriteSafetyMessage { get; set; } = "";
    public int CaseCount { get; set; }
    public int DeadlineCount { get; set; }
    public int ChecklistCount { get; set; }
    public int DiscoveryCount { get; set; }
    public int DocumentExportCount { get; set; }
    public bool SampleDataExists { get; set; }
    public string? LastImportResult { get; set; }
    public string? LastDocumentGenerationResult { get; set; }
    public string? StageMigrationReview { get; set; }
    public string? LatestLogPath { get; set; }
    public Dictionary<string, string> Folders { get; set; } = [];
}

public sealed class OrgDefaults
{
    public long Id { get; set; }
    public string? RowVersion { get; set; }
    public string AttorneyName { get; set; } = "";
    public string BarNumber { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string AddressLine1 { get; set; } = "";
    public string AddressLine2 { get; set; } = "";
    public string DivisionHeadName { get; set; } = "";
    public string RowSectionHeadName { get; set; } = "";
    public string ChiefLegalCounselName { get; set; } = "";
    public string? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed class DocumentTemplateField
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Type { get; set; } = "text";
    public string? DefaultValue { get; set; }
}

public sealed class DocumentTemplateInfo
{
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public List<DocumentTemplateField> ManualFields { get; set; } = [];
}

public sealed class TemplateTagInfo
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed class CustomDocumentTemplateInfo
{
    public long Id { get; set; }
    public string? RowVersion { get; set; }
    public string Key { get; set; } = "";
    public string BaseKey { get; set; } = "";
    public string Title { get; set; } = "";
    public string FileName { get; set; } = "";
    public string UploadedAt { get; set; } = "";
    public List<string> Tokens { get; set; } = [];
    public List<string> UnknownTokens { get; set; } = [];
    public List<DocumentTemplateField> ManualFields { get; set; } = [];
    // "docx" | "text" - docx templates merge tokens in place (preserving formatting/letterhead)
    // via DocumentGenerationEngine.FillDocxTemplate; text (.txt/.md) uses the plain FillTemplate
    // preview-then-edit-then-save flow that already existed.
    public string Format { get; set; } = "text";
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; }
    public string? StoragePath { get; set; }
    public string? UploadedBy { get; set; }
    // Unified catalog metadata.  These fields intentionally live on the existing
    // custom-template record so uploaded templates participate in the same case
    // library as built-in and discovery templates.
    public string Description { get; set; } = "";
    public string Category { get; set; } = "Other";
    public List<string> Tags { get; set; } = [];
    public string Visibility { get; set; } = "Personal";
    public string? OwnerUserId { get; set; }
    public string? DefaultOutputFileName { get; set; }
}

public sealed class UnifiedDocumentCatalogEntry
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "Other";
    public string Source { get; set; } = "BuiltIn";
    public string Format { get; set; } = "docx";
    public bool Active { get; set; } = true;
    public List<string> Tags { get; set; } = [];
    public int? Version { get; set; }
    public int? AvailableDiscoveryItemCount { get; set; }
    public string Description { get; set; } = "";
    public string Visibility { get; set; } = "Shared";
    public string? OwnerUserId { get; set; }
    public string? DefaultOutputFileName { get; set; }
}

public sealed class CustomTemplateTextUpdate
{
    public string Content { get; set; } = "";
}

public sealed class DocumentPreviewRequest
{
    public Dictionary<string, string> ManualInputs { get; set; } = [];
    public string? OutputFileName { get; set; }
}

public sealed class DocumentPreviewResult
{
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public List<string> MissingTokens { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<string> TemplateVersions { get; set; } = [];
    public List<string> IssueTags { get; set; } = [];
}

public sealed class DocxGenerationResult
{
    public DocumentExportRecord Record { get; set; } = new();
    public List<string> MissingTokens { get; set; } = [];
}

public sealed class SaveGeneratedDocumentRequest
{
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public string? BaseTemplateVersion { get; set; }
    public string? IssueTagVersions { get; set; }
    public string? MergeFieldValues { get; set; }
    public bool IsDraft { get; set; }
    public bool IsFinalized { get; set; } = true;
}

public sealed class ReferenceDocument
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Text { get; set; } = "";
}

public sealed class ReferenceDocumentUpdate
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Text { get; set; } = "";
}

// Manual (attorney-entered) inputs for one row of a Risk Analysis scenario.
// Mirrors columns B/F/G/H of the real Risk Analysis workbook for that row. The ledger is a
// fixed 5-row structure matching the office's real spreadsheet exactly: Landowner's Opinion of
// Value, Landowner's Appraisal, and three offer/counteroffer slots (each with a Split row).
// RowKey is a stable id (LandownerOpinionOfValue/LandownerAppraisal/AshcFirstOffer/
// AshcCounteroffer/LandownerCounteroffer) used to target patches - it is never displayed; the
// slot's current dropdown selection drives Label/OfferMaker independent of the key's name.
public sealed class RiskAnalysisRowInput
{
    public string RowKey { get; set; } = "";
    public string Label { get; set; } = "";
    // "ASHC" | "Landowner" - who made this offer/holds this position.
    public string OfferMaker { get; set; } = "Landowner";
    // When true, an extra computed "Split" row (midpoint of this row's amount and the total
    // deposited) is generated alongside this row.
    public bool IncludeSplit { get; set; }
    public decimal? JustCompensation { get; set; }
    public decimal LandownerFeesCosts { get; set; }
    public decimal AshcCosts { get; set; }
    public decimal HourlyFeesRisk { get; set; } = 40000;
}

// One live ledger per case (not a named/saveable "scenario" - the real workbook is a
// single sheet that gets periodically updated, so the app mirrors that: one record per
// case_id, always current).
public sealed class RiskAnalysisInput
{
    public long CaseId { get; set; }
    public string? RowVersion { get; set; }
    public string? Narrative { get; set; }
    public string? AnalysisDate { get; set; }
    public decimal InterestRate { get; set; } = 0.06m;
    public decimal ContingencyFeePercent { get; set; } = 0.30m;
    public List<RiskAnalysisRowInput> Rows { get; set; } = [];
}

// One computed output row (either a primary row or a computed "Split" row).
public sealed class RiskAnalysisRowResult
{
    public string RowKey { get; set; } = "";
    public string Label { get; set; } = "";
    public string OfferMaker { get; set; } = "Landowner";
    public bool IsSplit { get; set; }
    public decimal? JustCompensation { get; set; }
    public decimal? AmountAboveInitialDeposit { get; set; }
    public decimal? InterestOnOverage { get; set; }
    public decimal? Subtotal { get; set; }
    public decimal LandownerFeesCosts { get; set; }
    public decimal AshcCosts { get; set; }
    public decimal HourlyFeesRisk { get; set; }
    public decimal? ContingencyFee { get; set; }
    public decimal? TotalRiskHourly { get; set; }
    // "NotApplicable" (no just-compensation entered), "BelowThreshold" (didn't meet the 20% test), "Computed"
    public string HourlyRiskStatus { get; set; } = "NotApplicable";
    public decimal? TotalRiskContingency { get; set; }
    public string? Note { get; set; }
}

public sealed class RiskAnalysisResult
{
    public long Id { get; set; }
    public string? RowVersion { get; set; }
    public long CaseId { get; set; }
    public string? Narrative { get; set; }
    public decimal InitialDeposit { get; set; }
    public decimal AdditionalDeposit { get; set; }
    public decimal TotalDeposited { get; set; }
    public int? DaysSinceFiling { get; set; }
    public string AnalysisDate { get; set; } = "";
    public decimal InterestRate { get; set; } = 0.06m;
    public decimal ContingencyFeePercent { get; set; } = 0.30m;
    public List<RiskAnalysisRowResult> Rows { get; set; } = [];
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

// Immutable snapshot retained whenever an attorney saves the live ledger.
public sealed class RiskAnalysisHistoryRecord
{
    public long Id { get; set; }
    public string? RowVersion { get; set; }
    public long CaseId { get; set; }
    public string AnalysisDate { get; set; } = "";
    public string FormulaVersion { get; set; } = "risk-v1";
    public decimal InterestRate { get; set; } = 0.06m;
    public decimal ContingencyFeePercent { get; set; } = 0.30m;
    public string? KeyScenarioLabel { get; set; }
    public decimal? KeyScenarioValue { get; set; }
    public int? KeyScenarioOrder { get; set; }
    public string? Narrative { get; set; }
    public List<RiskAnalysisRowInput> Rows { get; set; } = [];
    public string CreatedAt { get; set; } = "";
}

public sealed class RiskAnalysisOfferLogEntry
{
    public long Id { get; set; }
    public string? RowVersion { get; set; }
    public long CaseId { get; set; }
    public string? OfferDate { get; set; }
    public string Party { get; set; } = "";
    public decimal? Amount { get; set; }
    public string? UpdatedAt { get; set; }
}

// Per-party service-of-process tracking - a case can have multiple defendants being served
// separately, each with their own method/status/attempt history, distinct from the single
// case-level servicePerfected/serviceMethod fields which only ever held one value for the case.
public sealed class ServiceLogEntry
{
    public long Id { get; set; }
    public long CaseId { get; set; }
    public string PartyName { get; set; } = "";
    public string Status { get; set; } = "Not Served";
    public string? Method { get; set; }
    public string? EventDate { get; set; }
    public string? Notes { get; set; }
    public string? CreatedAt { get; set; }
    public string? UpdatedAt { get; set; }
}

// One current-state appraisal position per side of the case ("ASHC" or "Landowner").
// Enforced as at most one row per (case, side) - editing always overwrites the same row.
public sealed class ValuationPositionRecord
{
    public long Id { get; set; }
    public string? RowVersion { get; set; }
    public long CaseId { get; set; }
    public string Side { get; set; } = "ASHC";
    public string? AppraiserName { get; set; }
    public decimal? AppraisedValue { get; set; }
    public string? ValueDate { get; set; }
    public string? Methodology { get; set; }
    public string? Notes { get; set; }
    public string? UpdatedAt { get; set; }
}

public sealed class ComparableSaleRecord
{
    public long Id { get; set; }
    public string? RowVersion { get; set; }
    public long CaseId { get; set; }
    public string Side { get; set; } = "ASHC";
    public string? SaleDescription { get; set; }
    public decimal? SalePrice { get; set; }
    public string? SaleDate { get; set; }
    public decimal? SizeAcres { get; set; }
    public string? AdjustmentNotes { get; set; }
    public string? Notes { get; set; }
}

// Fields the office's real settlement-posture narrative template needs that aren't tracked
// anywhere else in the app - collected via a one-time prompt when generating the narrative
// rather than persisted, per the user's explicit call. See BuildRiskNarrativeText.
public sealed class RiskNarrativeManualInputs
{
    public string? PropertyDescription { get; set; }
    public string? TceDescription { get; set; }
    public string? HighestAndBestUse { get; set; }
    public decimal? OurAppraisalLandBefore { get; set; }
    public decimal? OurAppraisalPerSfBefore { get; set; }
    public decimal? OurAppraisalLandAfter { get; set; }
    public decimal? OurAppraisalPerSfAfter { get; set; }
    public decimal? DefendantAppraisalLandBefore { get; set; }
    public decimal? DefendantAppraisalPerSfBefore { get; set; }
    public decimal? DefendantAppraisalLandAfter { get; set; }
    public decimal? DefendantAppraisalPerSfAfter { get; set; }
    public string? AshcOfferDate { get; set; }
    public decimal? FeeAdjustmentAmount { get; set; }
    public string? CounterofferDate { get; set; }
    public decimal? SettlementAmount { get; set; }
    public decimal? TrialFeeLow { get; set; }
    public decimal? TrialFeeHigh { get; set; }
}

public sealed class WitnessRecord
{
    public long Id { get; set; }
    public long CaseId { get; set; }
    public string Name { get; set; } = "";
    public string Side { get; set; } = "ASHC";
    public string? Role { get; set; }
    public string? ContactInfo { get; set; }
    public string SubpoenaStatus { get; set; } = "Not Needed";
    public string? OutlineNotes { get; set; }
    public string? Notes { get; set; }
    public string? RowVersion { get; set; }
    // Multi-user rollout Phase 3 (shared witness registry): links this per-case witness row to a
    // global witness_persons identity. Null means "not yet linked" (pre-migration legacy row, or
    // a save that hasn't resolved a person yet). Name/ContactInfo above remain a per-case
    // snapshot - they are populated FROM the linked person at save time but are not overwritten
    // just because the person's canonical record changes later.
    public long? PersonId { get; set; }
}

// Multi-user rollout Phase 3: the global witness *person* identity - one row per real person,
// shared across every case they're a witness in. Deliberately minimal (name + optional contact
// info) since the per-case witnesses table still carries all case-specific detail (side, role,
// subpoena status, outline notes). Full dual-provider parity, unlike the Phase 1/2 pieces that
// are SQL-Server-only functional - this table and its search/link behavior work fully on SQLite.
public sealed class WitnessPersonRecord
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? ContactInfo { get; set; }
    public string? RowVersion { get; set; }
}

// A witness_persons row as returned by the registry search/autofill endpoint, annotated with why
// it matched the typed query and (cheaply, via a join) which other case(s) it's already a
// witness in - so the picker can show "Maxwell Carter - also a witness in 24-CV-118" instead of
// just a bare name.
public sealed class WitnessPersonMatch
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? ContactInfo { get; set; }
    // "exact" = the typed text equals, starts with, or is contained in this person's name (the
    // common "just continuing to type the right name" case). "similar" = not exact, but flagged
    // by WitnessNameMatcher.AreSimilar (nickname/prefix or small edit distance) - a non-blocking
    // "did you mean" hint, never auto-selected.
    public string MatchType { get; set; } = "similar";
    public List<string> OtherCaseNumbers { get; set; } = [];
}

// Multi-user rollout Phase 4 (witness cross-reference lookup): the richer per-person detail
// behind GET /api/witness-registry/{personId}. Deliberately a simple lookup, not a conflict
// engine - the attorney glances at the returned dates and judges overlap themselves. No new date
// fields are stored anywhere for this: TrialDate/TrialEndDate are read live from the case's
// existing columns, and DepositionEvents are read live from that case's Deposition-type hearings
// rows - nothing here is re-entered or persisted redundantly on the witness or witness_persons
// row.
public sealed class WitnessPersonDetail
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? ContactInfo { get; set; }
    // Every current (not Closed/Complete/"Resolved / Closed") case where this person is linked as
    // a witness, INCLUDING the case the caller is currently viewing - the client filters that one
    // out before display since telling the attorney "also in this same case" is pointless.
    public List<WitnessPersonCase> Cases { get; set; } = [];
}

public sealed class WitnessPersonCase
{
    public long CaseId { get; set; }
    public string CaseNumber { get; set; } = "";
    public string CaseName { get; set; } = "";
    public string? TrialDate { get; set; }
    public string? TrialEndDate { get; set; }
    public List<WitnessDepositionEvent> DepositionEvents { get; set; } = [];
}

public sealed class WitnessDepositionEvent
{
    public string Title { get; set; } = "";
    public string? Date { get; set; }
}

public sealed class ExhibitRecord
{
    public long Id { get; set; }
    public long CaseId { get; set; }
    public string Label { get; set; } = "";
    public string Side { get; set; } = "ASHC";
    public string? Description { get; set; }
    public string Status { get; set; } = "Pre-Labeled";
    public string? Notes { get; set; }
    public string? RowVersion { get; set; }
}

public sealed class TrialMotionRecord
{
    public long Id { get; set; }
    public long CaseId { get; set; }
    public string Title { get; set; } = "";
    public string FiledBy { get; set; } = "ASHC";
    public string? FiledDate { get; set; }
    public string Status { get; set; } = "Pending";
    public string? Notes { get; set; }
    public string? RowVersion { get; set; }
}

public sealed class ImportResult
{
    public int RowsRead { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<string> Info { get; set; } = [];
}

public sealed class CaseWorkspaceResponse
{
    public CaseRecord Case { get; set; } = new();
    public List<DeadlineItem> Deadlines { get; set; } = [];
    public List<ChecklistItemRecord> ChecklistItems { get; set; } = [];
    public List<DiscoveryItemRecord> DiscoveryItems { get; set; } = [];
    public List<PublicationEntryRecord> PublicationEntries { get; set; } = [];
    public PublicationRecord Publication { get; set; } = new();
    public List<ServiceLogEntry> ServiceLogEntries { get; set; } = [];
    public List<OpposingAttorneyRecord> OpposingAttorneys { get; set; } = [];
    public List<CaseLegalAssistantRecord> CaseLegalAssistants { get; set; } = [];
    public List<CaseDefendantRecord> CaseDefendants { get; set; } = [];
    public List<PipelineHolderApprovalRecord> PipelineHolderApprovals { get; set; } = [];
    public List<CaseIssueTagRecord> CaseIssueTags { get; set; } = [];
    public List<IssueTagRecord> AvailableIssueTags { get; set; } = [];
    public List<CaseNoteRecord> CaseNotes { get; set; } = [];
    public List<HearingRecord> Hearings { get; set; } = [];
    public List<DocumentExportRecord> DocumentExports { get; set; } = [];
    public ServiceStatusSummary ServiceStatus { get; set; } = new();
    public DashboardData OverviewSummary { get; set; } = new();
}

// Build-plan step 3 (data model cutover): the unified document platform's own tables, replacing
// custom_document_templates / discovery_base_versions / discovery_template_items / document_exports
// / discovery_generations. See docs/document-system-audit-and-plan (Architecture) for the full design.

public sealed class DocumentTemplateRecord
{
    public long Id { get; set; }
    public string TemplateKey { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Category { get; set; } = "Other";
    public string? DocumentType { get; set; }
    public bool IsBuiltin { get; set; }
    public bool IsDeleted { get; set; }
    public string CreatedAt { get; set; } = "";
    public string? CreatedBy { get; set; }
}

public sealed class DocumentTemplateVersionRecord
{
    public long Id { get; set; }
    public long TemplateId { get; set; }
    public int Version { get; set; }
    public string StoragePath { get; set; } = "";
    public List<string> Tokens { get; set; } = [];
    public List<string> UnknownTokens { get; set; } = [];
    public bool IsActive { get; set; }
    public string CreatedAt { get; set; } = "";
    public string? CreatedBy { get; set; }
}

public sealed class DocumentRuntimeInputRecord
{
    public long Id { get; set; }
    public long TemplateVersionId { get; set; }
    public string FieldKey { get; set; } = "";
    public string Label { get; set; } = "";
    public string FieldType { get; set; } = "text";
    public bool IsRequired { get; set; } = true;
    public int SortOrder { get; set; }
}

public sealed class DocumentTemplateSectionRecord
{
    public long Id { get; set; }
    public long TemplateVersionId { get; set; }
    public string SectionKey { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Description { get; set; }
    public string? IssueTagName { get; set; }
    public int SortOrder { get; set; }
}

public sealed class DocumentSectionOverlapRecord
{
    public long Id { get; set; }
    public long SectionAId { get; set; }
    public long SectionBId { get; set; }
    public string? Note { get; set; }
}

public sealed class DocumentGenerationRecord
{
    public long Id { get; set; }
    public long CaseId { get; set; }
    public long TemplateId { get; set; }
    public long TemplateVersionId { get; set; }
    public string OutputPath { get; set; } = "";
    public string RenderedAt { get; set; } = "";
    public string? GeneratedBy { get; set; }
    public List<string> SectionsIncluded { get; set; } = [];
    public string RuntimeInputValuesJson { get; set; } = "{}";
    public bool IsDraft { get; set; } = true;
    public bool IsFinalized { get; set; }
    public List<string> MissingFields { get; set; } = [];
}

// Lists a case's document_generations rows for the unified "Generated Documents" history view -
// merged client-side with the legacy document_exports list rather than migrated into one schema,
// since most legacy rows (Case Summary/Review, retired custom templates) have no template to
// attach to in document_generations. Carries the template title (not on DocumentGenerationRecord)
// since that's what the history list actually displays.
public sealed class DocumentGenerationHistoryItem
{
    public long Id { get; set; }
    public string TemplateTitle { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public string RenderedAt { get; set; } = "";
    public string? GeneratedBy { get; set; }
    public bool IsDraft { get; set; } = true;
    public bool IsFinalized { get; set; }
    public List<string> MissingFields { get; set; } = [];
}

// Build-plan step 4 (unified case UI): the checklist the generation form shows before rendering -
// every named section available for this template, pre-checked from the case's actual issue
// tags, freely togglable, with overlap warnings surfaced up front rather than discovered after
// the fact. See docs/document-system-audit-and-plan (Architecture, "Tag-driven content: a
// checklist, not an invisible switch").
public sealed class DocumentGenerationChecklistItem
{
    public string SectionKey { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Description { get; set; }
    public string? IssueTagName { get; set; }
    public bool IsDefaultChecked { get; set; }
    public List<string> OverlapWarnings { get; set; } = [];
}

public sealed class DocumentGenerationChecklist
{
    public string TemplateKey { get; set; } = "";
    public string Title { get; set; } = "";
    public int TemplateVersion { get; set; }
    public List<DocumentGenerationChecklistItem> Sections { get; set; } = [];
    public List<DocumentRuntimeInputRecord> RuntimeInputs { get; set; } = [];
}

public sealed class DocumentGenerationRequest
{
    public List<string> SelectedSectionKeys { get; set; } = [];
    public Dictionary<string, string> RuntimeInputValues { get; set; } = [];
    public string? OutputFileName { get; set; }
}

public sealed class DocumentGenerationResult
{
    public long GenerationId { get; set; }
    public string OutputPath { get; set; } = "";
    public List<string> SectionsIncluded { get; set; } = [];
    public List<string> MissingFields { get; set; } = [];
}

// Build-plan step 5 (unified Settings UI): the admin-side models for Document Templates and
// Issue Tags. Separate from the generation-time models above - these describe how a template is
// configured, not how a specific generation was resolved.

public sealed class DocumentSectionOverlapPair
{
    public string SectionAKey { get; set; } = "";
    public string SectionBKey { get; set; } = "";
    public string? Note { get; set; }
}

public sealed class DocumentTemplateAdminSummary
{
    public DocumentTemplateRecord Template { get; set; } = new();
    public DocumentTemplateVersionRecord? ActiveVersion { get; set; }
    public List<DocumentTemplateVersionRecord> Versions { get; set; } = [];
    public List<DocumentTemplateSectionRecord> Sections { get; set; } = [];
    public List<DocumentSectionOverlapPair> Overlaps { get; set; } = [];
    public List<DocumentRuntimeInputRecord> RuntimeInputs { get; set; } = [];
    public List<string> LintIssues { get; set; } = [];
}

public sealed class DocumentTemplateConfigurationRequest
{
    public List<DocumentTemplateSectionRecord> Sections { get; set; } = [];
    public List<DocumentSectionOverlapPair> Overlaps { get; set; } = [];
    public List<DocumentRuntimeInputRecord> RuntimeInputs { get; set; } = [];
}

public sealed class IssueTagUsage
{
    public string TagName { get; set; } = "";
    public List<string> TemplateTitles { get; set; } = [];
}
