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
}
