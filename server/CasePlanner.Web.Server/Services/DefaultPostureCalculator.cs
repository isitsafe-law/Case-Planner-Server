using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Services;

// Eminent-domain condemnation cases routinely end up in a "default judgment" posture - most
// commonly when serving a group of heirs, some or all of whom never answer. Before this, nothing
// in the app tracked whether an answer had ever been filed; the only signal was the generic
// "No-Answer Default Judgment Checkpoint" deadline template (DeadlineTemplateSeeds), which just
// showed up in the deadline list like any other reminder with no dashboard visibility. This is the
// pure derivation behind the visible warning badge (case list + case workspace header): no answer
// on file, service perfected, and NoAnswerThresholdDays have elapsed since. Deliberately not a new
// manually-set status field (a prior cases.track field tried that shape and ended up permanently
// orphaned from the UI) - it's derived from the one added fact (CaseRecord.AnswerFiled/
// AnswerFiledDate) plus the existing ServicePerfectedDate, same style as CaseAttentionEngine.Compute.
public static class DefaultPostureCalculator
{
    // Single source of truth for the day-count, shared with the "No-Answer Default Judgment
    // Checkpoint" deadline template's OffsetDays so the deadline reminder and the derived warning
    // badge can never drift apart.
    public const int NoAnswerThresholdDays = 180;

    public static bool IsLikelyDefault(bool answerFiled, string? servicePerfectedDate, DateOnly asOfDate)
    {
        if (answerFiled)
        {
            return false;
        }

        if (!DateOnly.TryParse(servicePerfectedDate, out var perfected))
        {
            return false;
        }

        return asOfDate.DayNumber - perfected.DayNumber >= NoAnswerThresholdDays;
    }

    // Once a case has real per-defendant rows (CaseDefendantRecord/case_defendants), the
    // case-level AnswerFiled boolean above stops being the source of truth for that case - a
    // single global toggle can't represent multiple defendants (often heirs) answering at
    // genuinely different times. This overload is the defendant-list-driven replacement: same
    // service-perfected + NoAnswerThresholdDays gate as above, but "no answer on file" is now
    // "at least one defendant who was actually served has no answer filed" rather than a single
    // flag. A defendant who was only served by Warning Order with no address (the Unknown Heirs
    // service method, used when a specific heir can't be located/identified) is excluded from
    // that check - there's no one there to have failed to answer, so their row shouldn't drive a
    // default-posture warning on its own. Call-site branching on whether the case has any
    // defendant rows at all (falling back to the legacy single-bool overload above when it does
    // not) lives at the two stamp sites - CasePlannerRepository.ApplyCaseAttentionAsync and
    // SqlServerWorkspaceQuery.GetWorkspaceAsync/GetDashboardAsync - not here, so this stays a pure
    // function of the defendant list actually passed in.
    public static bool IsLikelyDefault(IReadOnlyList<CaseDefendantRecord> defendants, string? servicePerfectedDate, DateOnly asOfDate)
    {
        if (!DateOnly.TryParse(servicePerfectedDate, out var perfected))
        {
            return false;
        }

        if (asOfDate.DayNumber - perfected.DayNumber < NoAnswerThresholdDays)
        {
            return false;
        }

        return defendants.Any(d => WasActuallyServed(d) && !d.AnswerFiled);
    }

    // A Warning-Order-only entry with no address represents an heir the plaintiff could not
    // locate or identify - there's no one there who could have answered, so it shouldn't count as
    // "served but silent" for default-posture purposes. Any other service method, or any entry
    // that does carry an address (even alongside a Warning Order, e.g. a partially-known heir),
    // counts as actually served.
    public static bool WasActuallyServed(CaseDefendantRecord defendant) =>
        !string.IsNullOrWhiteSpace(defendant.Address) ||
        !string.Equals(defendant.ServiceMethod, "Warning Order", StringComparison.OrdinalIgnoreCase);
}
