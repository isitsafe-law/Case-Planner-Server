using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Services;

// Computes a per-case triage signal for the Dashboard's Work Queue: why a case needs attention,
// how urgent, and what to do next. Deliberately separate from CaseAttentionEngine (which drives
// the Cases list / case workspace attention pill and must not change behavior here) - this is
// purely additive, dashboard-only logic layered on top of data GetDashboardAsync already loads.
public static class DashboardTriageEngine
{
    // Lookahead windows - kept as named constants per the original design brief so they're easy
    // to retune without hunting through the evaluation logic below.
    public const int HardDeadlineLookaheadDays = 14;
    public const int CourtEventLookaheadDays = 30;

    // Stage-specific "days without activity before a case needs review" thresholds, keyed to
    // the 5 high-level stages - a judgment call, not measured data, and easy to retune below.
    private static readonly Dictionary<string, int> StaleThresholdDaysByStage = new(StringComparer.Ordinal)
    {
        ["Intake & Filing"] = 14,
        ["Service"] = 14,
        ["Discovery & Evaluation"] = 30,
        ["Trial Track"] = 45,
        ["Resolved"] = 60,
    };
    private const int DefaultStaleThresholdDays = 90; // no stage set - "quiet but healthy" default
    private const int TrialPrepFarOutThresholdDays = 90; // relaxes Trial Track's default 45 days
    private const int TrialFarOutDays = 365; // ...once the trial itself is more than a year out

    // Lower runs first. Tiers follow the brief's 8-level priority list, collapsed onto the 6
    // categories actually shown to the user (a "quiet, healthy case" - tier 8 - just has no entry).
    private const int PriorityCourtEventSoon = 0;
    private const int PriorityHardDeadlineOverdue = 10;
    private const int PriorityServiceRiskOverdue = 20;
    private const int PriorityHardDeadlineSoon = 30;
    private const int PriorityServiceRiskOther = 40;
    private const int PriorityChecklistDue = 50;
    private const int PriorityBlocked = 60;
    private const int PriorityStaleReview = 70;

    private sealed record Signal(int Priority, string Category, string Reason, string Timing, string NextAction, string? DueDate);

    public static DashboardTriageEntry? Evaluate(
        CaseRecord caseRecord,
        IReadOnlyList<DeadlineItem> caseDeadlines,
        IReadOnlyList<ChecklistItemRecord> caseChecklist,
        IReadOnlyList<DiscoveryItemRecord> caseDiscovery,
        ServiceQueueItem? serviceSummary,
        IReadOnlyList<HearingRecord> caseHearings,
        DateOnly today,
        bool discoveryComplete = false)
    {
        if (DateOnly.TryParse(caseRecord.DeferredUntil, out var deferredUntil) && deferredUntil > today)
        {
            return null;
        }

        var signals = new List<Signal>();

        AddCourtEventSignal(signals, caseRecord, caseHearings, today);
        var openDeadlines = caseDeadlines.Where(d => d.Status is not ("Done" or "Complete")).ToList();
        AddOverdueDeadlineSignal(signals, openDeadlines, today);
        AddServiceRiskSignal(signals, serviceSummary);
        AddDeadlineSoonSignal(signals, openDeadlines, today);
        AddChecklistDueSignal(signals, caseChecklist, today);
        if (!discoveryComplete) AddBlockedSignal(signals, caseDiscovery, today);
        AddStaleReviewSignal(signals, caseRecord, today);

        if (signals.Count == 0)
        {
            return null;
        }

        var primary = signals.OrderBy(s => s.Priority).First();
        return new DashboardTriageEntry
        {
            CaseId = caseRecord.Id,
            CaseName = caseRecord.CaseName,
            CaseNumber = caseRecord.CaseNumber,
            Category = primary.Category,
            Reason = primary.Reason,
            Timing = primary.Timing,
            Stage = caseRecord.Stage,
            Track = caseRecord.Track,
            NextAction = primary.NextAction,
            DueDate = primary.DueDate,
            PriorityScore = primary.Priority,
            MatchedCategories = signals.Select(s => s.Category).Distinct().ToList()
        };
    }

    private static void AddCourtEventSignal(List<Signal> signals, CaseRecord caseRecord, IReadOnlyList<HearingRecord> caseHearings, DateOnly today)
    {
        DateOnly? soonest = null;
        string? label = null;
        if (DateOnly.TryParse(caseRecord.TrialDate, out var trial) && trial >= today && trial <= today.AddDays(CourtEventLookaheadDays))
        {
            soonest = trial;
            label = "Trial / hearing date";
        }

        foreach (var hearing in caseHearings)
        {
            if (DateOnly.TryParse(hearing.HearingDate, out var hearingDate) && hearingDate >= today && hearingDate <= today.AddDays(CourtEventLookaheadDays)
                && (soonest is null || hearingDate < soonest))
            {
                soonest = hearingDate;
                label = string.IsNullOrWhiteSpace(hearing.Title) ? "Hearing" : hearing.Title;
            }
        }

        if (soonest is not { } eventDate)
        {
            return;
        }

        var daysOut = eventDate.DayNumber - today.DayNumber;
        signals.Add(new Signal(
            PriorityCourtEventSoon, "courtEventsSoon",
            $"{label} approaching",
            daysOut == 0 ? "Today" : $"In {daysOut} day{(daysOut == 1 ? "" : "s")}",
            "Prepare trial/hearing checklist",
            eventDate.ToString("yyyy-MM-dd")));
    }

    private static void AddOverdueDeadlineSignal(List<Signal> signals, List<DeadlineItem> openDeadlines, DateOnly today)
    {
        var overdue = openDeadlines
            .Where(d => DateOnly.TryParse(d.DueDate, out var due) && due <= today)
            .OrderBy(d => d.DueDate, StringComparer.Ordinal)
            .FirstOrDefault();
        if (overdue is null)
        {
            return;
        }

        DateOnly.TryParse(overdue.DueDate, out var due);
        var daysLate = today.DayNumber - due.DayNumber;
        signals.Add(new Signal(
            PriorityHardDeadlineOverdue, "needsActionNow",
            $"Hard deadline missed: {overdue.Title}",
            daysLate <= 0 ? "Due today" : $"Overdue by {daysLate} day{(daysLate == 1 ? "" : "s")}",
            "Resolve overdue deadline",
            overdue.DueDate));
    }

    private static void AddServiceRiskSignal(List<Signal> signals, ServiceQueueItem? serviceSummary)
    {
        if (serviceSummary is null || serviceSummary.WarningLevel is not ("overdue" or "urgent" or "upcoming" or "missing"))
        {
            return;
        }

        var priority = serviceSummary.WarningLevel == "overdue" ? PriorityServiceRiskOverdue : PriorityServiceRiskOther;
        var timing = serviceSummary.WarningLevel switch
        {
            "overdue" => serviceSummary.DaysRemaining is { } overdueDays ? $"Overdue by {Math.Abs(overdueDays)} day{(Math.Abs(overdueDays) == 1 ? "" : "s")}" : "Overdue",
            "missing" => "Review due",
            _ => serviceSummary.DaysRemaining is { } dueDays ? $"Due in {dueDays} day{(dueDays == 1 ? "" : "s")}" : "Review due",
        };
        signals.Add(new Signal(
            priority, "serviceRisk", serviceSummary.WarningText, timing,
            serviceSummary.WarningLevel == "missing" ? "Set a service deadline" : "Confirm service attempt / record proof",
            serviceSummary.ServiceDeadline120));
    }

    private static void AddDeadlineSoonSignal(List<Signal> signals, List<DeadlineItem> openDeadlines, DateOnly today)
    {
        // Already-overdue deadlines are covered by AddOverdueDeadlineSignal at a higher priority -
        // this only looks at the window strictly after today.
        var soon = openDeadlines
            .Where(d => DateOnly.TryParse(d.DueDate, out var due) && due > today && due <= today.AddDays(HardDeadlineLookaheadDays))
            .OrderBy(d => d.DueDate, StringComparer.Ordinal)
            .FirstOrDefault();
        if (soon is null)
        {
            return;
        }

        DateOnly.TryParse(soon.DueDate, out var due);
        var daysOut = due.DayNumber - today.DayNumber;
        signals.Add(new Signal(
            PriorityHardDeadlineSoon, "hardDeadlinesSoon",
            $"Hard deadline due soon: {soon.Title}",
            $"Due in {daysOut} day{(daysOut == 1 ? "" : "s")}",
            "Review upcoming deadline",
            soon.DueDate));
    }

    private static void AddChecklistDueSignal(List<Signal> signals, IReadOnlyList<ChecklistItemRecord> caseChecklist, DateOnly today)
    {
        var overdue = caseChecklist
            .Where(i => i.Status is not ("Done" or "Complete" or "N/A") && DateOnly.TryParse(i.DueDate, out var due) && due <= today)
            .OrderBy(i => i.DueDate, StringComparer.Ordinal)
            .FirstOrDefault();
        if (overdue is null)
        {
            return;
        }

        DateOnly.TryParse(overdue.DueDate, out var due);
        var daysLate = today.DayNumber - due.DayNumber;
        signals.Add(new Signal(
            PriorityChecklistDue, "needsActionNow",
            $"Task due: {overdue.Task}",
            daysLate <= 0 ? "Due today" : $"Overdue by {daysLate} day{(daysLate == 1 ? "" : "s")}",
            "Complete checklist task",
            overdue.DueDate));
    }

    private static void AddBlockedSignal(List<Signal> signals, IReadOnlyList<DiscoveryItemRecord> caseDiscovery, DateOnly today)
    {
        // Proxy for "blocked on an outside party": an open discovery item explicitly waiting on
        // the other side, whose own follow-up target date has already passed. There's no general
        // case-level "blocked" flag in the data model yet - see DashboardTriageEntry doc comment.
        var blocked = caseDiscovery
            .Where(d => (d.Status.Contains("Waiting", StringComparison.OrdinalIgnoreCase) || d.Status.Contains("Follow-Up", StringComparison.OrdinalIgnoreCase))
                        && DateOnly.TryParse(d.FollowUpDate ?? d.DueDate, out var due) && due < today)
            .OrderBy(d => d.FollowUpDate ?? d.DueDate, StringComparer.Ordinal)
            .FirstOrDefault();
        if (blocked is null)
        {
            return;
        }

        DateOnly.TryParse(blocked.FollowUpDate ?? blocked.DueDate, out var due);
        var daysLate = today.DayNumber - due.DayNumber;
        signals.Add(new Signal(
            PriorityBlocked, "blocked",
            $"Waiting on other party: {blocked.Direction} {blocked.DiscoveryType}",
            $"Overdue by {daysLate} day{(daysLate == 1 ? "" : "s")}",
            "Follow up with opposing party",
            blocked.FollowUpDate ?? blocked.DueDate));
    }

    private static void AddStaleReviewSignal(List<Signal> signals, CaseRecord caseRecord, DateOnly today)
    {
        var threshold = GetStaleThresholdDays(caseRecord, today);
        var lastActivityIso = caseRecord.LastActivityAt;
        if (!string.IsNullOrEmpty(lastActivityIso) && lastActivityIso.Length >= 10 && DateOnly.TryParse(lastActivityIso[..10], out var lastActivityDate))
        {
            var daysSinceActivity = today.DayNumber - lastActivityDate.DayNumber;
            if (daysSinceActivity >= threshold)
            {
                signals.Add(new Signal(PriorityStaleReview, "staleReview", $"No meaningful activity in {daysSinceActivity} days", "Review due", "Review case status", null));
            }

            return;
        }

        // No activity timestamp at all (shouldn't happen once a case has been saved even once,
        // but a freshly-imported row could lack one) - silence is silence whether or not it can
        // be dated precisely, so this counts as stale too.
        signals.Add(new Signal(PriorityStaleReview, "staleReview", "No recorded case activity", "Review due", "Review case status", null));
    }

    private static int GetStaleThresholdDays(CaseRecord caseRecord, DateOnly today)
    {
        if (string.IsNullOrEmpty(caseRecord.Stage) || !StaleThresholdDaysByStage.TryGetValue(caseRecord.Stage, out var days))
        {
            return DefaultStaleThresholdDays;
        }

        if (caseRecord.Stage == "Trial Track" && DateOnly.TryParse(caseRecord.TrialDate, out var trial) && trial.DayNumber - today.DayNumber > TrialFarOutDays)
        {
            return TrialPrepFarOutThresholdDays;
        }

        return days;
    }
}
