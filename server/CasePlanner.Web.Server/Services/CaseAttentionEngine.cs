using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Services;

// Computes a per-case attention rollup shared by the case list table and the
// dashboard "cases requiring attention" list, so the two never drift apart.
// Priority: a closed case is always "closed" regardless of any leftover open
// deadlines (calendar urgency is moot once the matter is done); otherwise a
// genuinely recent open critical deadline ("urgent") outranks urgent-severity
// ("attention"), which outranks "unconfirmed", which outranks "stalled" (no
// activity in StalledDays), which outranks "on track".
public static class CaseAttentionEngine
{
    public const int StalledDays = 30;

    // GenerateDeadlinesForCaseAsync already closes a critical service deadline the moment there's
    // real corroborating signal (service confirmed, stage past Service, or case closed) - so any
    // critical deadline still open here has none. Below this age it's still a plausible live risk
    // and stays "urgent"; past it, a real case can't credibly still be sitting unserved, so it's
    // almost certainly just a historical import gap. Downgrading to "unconfirmed" keeps it visible
    // without asserting it's actually resolved.
    public const int UnconfirmedAfterDays = 365;

    public static (string Status, string? NextDeadlineDate, string? NextDeadlineTitle) Compute(
        IReadOnlyList<DeadlineItem> caseDeadlines, string? lastActivityIso, string? caseStatus = null)
    {
        if (caseStatus is "Closed" or "Complete")
        {
            return ("closed", null, null);
        }

        // Freshly imported, not yet confirmed through the triage wizard - no deadlines exist yet
        // and no attention judgment is meaningful until intake completes.
        if (caseStatus == "Triage")
        {
            return ("triage", null, null);
        }

        var openDeadlines = caseDeadlines.Where(d => d.Status is not ("Done" or "Complete")).ToList();
        var staleCutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-UnconfirmedAfterDays);
        // Applies to any severity, not just critical: the 31/60/90/120-day service reminders are
        // siblings generated (and left open) together, so a "critical" 120-day deadline being this
        // stale means its "urgent"-severity 90-day watch-alert sibling is just as stale and just as
        // uninformative - both should fall into "unconfirmed" together, not have the watch alert
        // alone keep the case pinned at "attention".
        bool IsStale(DeadlineItem d) => DateOnly.TryParse(d.DueDate, out var due) && due < staleCutoff;

        var hasCritical = openDeadlines.Any(d => d.Severity == "critical" && !IsStale(d));
        var hasUrgent = openDeadlines.Any(d => d.Severity == "urgent" && !IsStale(d));
        var hasUnconfirmed = openDeadlines.Any(d => d.Severity is "critical" or "urgent" && IsStale(d));

        var cutoff = DateTime.UtcNow.AddDays(-StalledDays).ToString("O");
        var stalled = string.IsNullOrEmpty(lastActivityIso) || string.CompareOrdinal(lastActivityIso, cutoff) < 0;

        var status = hasCritical ? "urgent" : hasUrgent ? "attention" : hasUnconfirmed ? "unconfirmed" : stalled ? "stalled" : "onTrack";

        var next = openDeadlines
            .Where(d => !string.IsNullOrEmpty(d.DueDate))
            .OrderBy(d => d.DueDate, StringComparer.Ordinal)
            .FirstOrDefault();

        return (status, next?.DueDate, next?.Title);
    }
}
