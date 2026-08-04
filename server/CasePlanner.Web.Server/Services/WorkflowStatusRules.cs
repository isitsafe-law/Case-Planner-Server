namespace CasePlanner.Web.Server.Services;

// Centralized definition of "not yet a real litigation matter" - Triage (freshly imported, not yet
// confirmed through the triage wizard) or Pipeline. Deadline/checklist generation must never run
// for either. Pre-filing ROW-intake tracts (MatterType="PreFilingTract") stay CaseStatus="Pipeline"
// for their entire pre-filing lifecycle (see CaseRecord.RowIntakeStatus for their ROW-specific
// sub-stage), so they are covered by this same rule automatically - no separate check is needed as
// new pre-filing sub-stages are added.
//
// Previously duplicated separately across CasePlannerRepository (SQLite) and
// WorkflowGenerationService (SQL Server pilot), with a real gap: the deadlines-specific SQLite
// check only looked at the legacy Status column, not CaseStatus. Since a PreFilingTract case has
// Status="Active" (not "Pipeline") while CaseStatus="Pipeline", that meant deadline generation was
// not actually suppressed for pre-filing tracts before this fix - checking both, in one place,
// closes that gap for good rather than needing to remember it at every future call site.
public static class WorkflowStatusRules
{
    public static bool IsPreFiling(string? status, string? caseStatus) =>
        status is "Triage" or "Pipeline" || caseStatus is "Triage" or "Pipeline";

    // Valid CaseRecord.RowIntakeStatus values, in their normal progression order. "Acquired by
    // Agreement", "Project Revised", and "Withdrawn" are terminal (the tract is never filed);
    // "Returned to ROW" can cycle back into "In Title Review" on resubmission.
    public static readonly string[] RowIntakeStatuses =
    [
        "Received from ROW", "In Title Review", "Returned to ROW", "Ready for Assignment",
        "Acquired by Agreement", "Project Revised", "Withdrawn"
    ];

    public static readonly string[] RowIntakeTerminalStatuses =
        ["Acquired by Agreement", "Project Revised", "Withdrawn"];

    // Cases parked with ROW (sent back for correction, not currently anyone in the firm's active
    // work) or at a terminal ROW outcome (never filed) drop out of the Attorney/Legal Assistant
    // dashboards' "active" set - same exclusion the dashboards already apply for Closed/Complete/
    // Triage cases. Still fully queryable/visible in the general case list and by direct search;
    // this only trims the dashboard worklists.
    public static bool IsRowIntakeInactive(string? rowIntakeStatus) =>
        rowIntakeStatus == "Returned to ROW" || RowIntakeTerminalStatuses.Contains(rowIntakeStatus);
}
