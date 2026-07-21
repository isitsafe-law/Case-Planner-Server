namespace CasePlanner.Web.Server.Services;

// Multi-user rollout Phase 4b (deadline reminders): the pure, DB-free "which cases are due for a
// reminder today" decision, pulled out the same way WitnessNameMatcher was in Phase 3 - so the one
// piece of this trigger's logic that can be meaningfully unit-tested gets real, direct coverage even
// though the surrounding feature (case_assignments-based recipients, actual notification creation)
// is SQL-Server-only and compile/review-only here. Takes plain case deadline data + today's date +
// lead-time days + a set of already-notified (caseId, deadlineType, deadlineDate) keys, and returns
// exactly which combinations are newly due - no DB, no I/O, no clock access of its own.
public sealed record CaseDeadlineSnapshot(long CaseId, string CaseNumber, DateOnly? TrialDate, DateOnly? ServiceDeadline120);

public sealed record DueDeadlineReminder(long CaseId, string CaseNumber, string DeadlineType, DateOnly DeadlineDate);

public static class DeadlineReminderScanner
{
    public const string TrialDateType = "TrialDate";
    public const string ServiceDeadline120Type = "ServiceDeadline120";

    /// <summary>Which (caseId, deadlineType, deadlineDate) combinations just became due for a
    /// reminder. A case's TrialDate and ServiceDeadline120 are checked independently - a case can be
    /// due for one, the other, both, or neither in the same scan. "Due" means the deadline falls
    /// exactly leadDays after today (not "on or before"): a scan that runs more than once a day, or
    /// that missed a day, relies on the alreadyNotified set - not a "less than or equal" window - to
    /// stay idempotent, since the caller is expected to record every combination it's ever notified
    /// for. If a case's deadline date changes after a reminder was already sent for the old date,
    /// the new date is a different key and is correctly treated as newly due.</summary>
    public static IReadOnlyList<DueDeadlineReminder> GetDueReminders(
        IEnumerable<CaseDeadlineSnapshot> cases,
        DateOnly today,
        int leadDays,
        IReadOnlySet<(long CaseId, string DeadlineType, DateOnly DeadlineDate)> alreadyNotified)
    {
        var targetDate = today.AddDays(leadDays);
        var due = new List<DueDeadlineReminder>();
        foreach (var c in cases)
        {
            TryAdd(c.CaseId, c.CaseNumber, TrialDateType, c.TrialDate, targetDate, alreadyNotified, due);
            TryAdd(c.CaseId, c.CaseNumber, ServiceDeadline120Type, c.ServiceDeadline120, targetDate, alreadyNotified, due);
        }

        return due;
    }

    private static void TryAdd(
        long caseId,
        string caseNumber,
        string deadlineType,
        DateOnly? deadlineDate,
        DateOnly targetDate,
        IReadOnlySet<(long CaseId, string DeadlineType, DateOnly DeadlineDate)> alreadyNotified,
        List<DueDeadlineReminder> due)
    {
        if (deadlineDate is null || deadlineDate.Value != targetDate)
        {
            return;
        }

        if (alreadyNotified.Contains((caseId, deadlineType, deadlineDate.Value)))
        {
            return;
        }

        due.Add(new DueDeadlineReminder(caseId, caseNumber, deadlineType, deadlineDate.Value));
    }
}
