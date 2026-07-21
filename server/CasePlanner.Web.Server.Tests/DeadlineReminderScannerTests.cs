using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Tests;

// Multi-user rollout Phase 4b (deadline reminders): pure, DB-free coverage of the "which cases are
// due for a reminder today" decision. This is the one piece of the deadline-reminder trigger that
// gets real unit tests - everything downstream (case_assignments recipient resolution, the actual
// BackgroundService, notification creation, email sending) needs SQL Server and is compile/review-
// only, same as the rest of this rollout's SqlServer*-prefixed code.
public class DeadlineReminderScannerTests
{
    private static readonly DateOnly Today = new(2026, 7, 20);
    private static readonly HashSet<(long, string, DateOnly)> NoneNotified = [];

    [Fact]
    public void GetDueReminders_TrialDateExactlyLeadDaysOut_Fires()
    {
        var trialDate = Today.AddDays(7);
        var cases = new[] { new CaseDeadlineSnapshot(1, "24-CV-100", trialDate, null) };

        var due = DeadlineReminderScanner.GetDueReminders(cases, Today, 7, NoneNotified);

        var reminder = Assert.Single(due);
        Assert.Equal(1, reminder.CaseId);
        Assert.Equal("24-CV-100", reminder.CaseNumber);
        Assert.Equal(DeadlineReminderScanner.TrialDateType, reminder.DeadlineType);
        Assert.Equal(trialDate, reminder.DeadlineDate);
    }

    [Fact]
    public void GetDueReminders_TrialDateOneDayShortOfLeadTime_DoesNotFire()
    {
        var cases = new[] { new CaseDeadlineSnapshot(1, "24-CV-100", Today.AddDays(6), null) };

        var due = DeadlineReminderScanner.GetDueReminders(cases, Today, 7, NoneNotified);

        Assert.Empty(due);
    }

    [Fact]
    public void GetDueReminders_TrialDateOneDayPastLeadTime_DoesNotFire()
    {
        var cases = new[] { new CaseDeadlineSnapshot(1, "24-CV-100", Today.AddDays(8), null) };

        var due = DeadlineReminderScanner.GetDueReminders(cases, Today, 7, NoneNotified);

        Assert.Empty(due);
    }

    [Fact]
    public void GetDueReminders_TrialDateAndServiceDeadline_CheckedIndependently_OnlyTrialDateDue()
    {
        var cases = new[]
        {
            new CaseDeadlineSnapshot(1, "24-CV-100", Today.AddDays(7), Today.AddDays(30)),
        };

        var due = DeadlineReminderScanner.GetDueReminders(cases, Today, 7, NoneNotified);

        var reminder = Assert.Single(due);
        Assert.Equal(DeadlineReminderScanner.TrialDateType, reminder.DeadlineType);
    }

    [Fact]
    public void GetDueReminders_TrialDateAndServiceDeadline_CheckedIndependently_OnlyServiceDeadlineDue()
    {
        var cases = new[]
        {
            new CaseDeadlineSnapshot(1, "24-CV-100", Today.AddDays(30), Today.AddDays(7)),
        };

        var due = DeadlineReminderScanner.GetDueReminders(cases, Today, 7, NoneNotified);

        var reminder = Assert.Single(due);
        Assert.Equal(DeadlineReminderScanner.ServiceDeadline120Type, reminder.DeadlineType);
    }

    [Fact]
    public void GetDueReminders_BothDatesDueOnTheSameScan_BothFire()
    {
        var dueDate = Today.AddDays(7);
        var cases = new[] { new CaseDeadlineSnapshot(1, "24-CV-100", dueDate, dueDate) };

        var due = DeadlineReminderScanner.GetDueReminders(cases, Today, 7, NoneNotified);

        Assert.Equal(2, due.Count);
        Assert.Contains(due, d => d.DeadlineType == DeadlineReminderScanner.TrialDateType);
        Assert.Contains(due, d => d.DeadlineType == DeadlineReminderScanner.ServiceDeadline120Type);
    }

    [Fact]
    public void GetDueReminders_CaseWithNeitherDateSet_ProducesNothing()
    {
        var cases = new[] { new CaseDeadlineSnapshot(1, "24-CV-100", null, null) };

        var due = DeadlineReminderScanner.GetDueReminders(cases, Today, 7, NoneNotified);

        Assert.Empty(due);
    }

    [Fact]
    public void GetDueReminders_AlreadyNotifiedCombination_ExcludedEvenWhenStillDue()
    {
        var trialDate = Today.AddDays(7);
        var cases = new[] { new CaseDeadlineSnapshot(1, "24-CV-100", trialDate, null) };
        var alreadyNotified = new HashSet<(long, string, DateOnly)> { (1, DeadlineReminderScanner.TrialDateType, trialDate) };

        var due = DeadlineReminderScanner.GetDueReminders(cases, Today, 7, alreadyNotified);

        // Idempotent on repeat scans - a second scan the same day (or a restarted service scanning
        // again) must not re-notify for the same case+type+date.
        Assert.Empty(due);
    }

    [Fact]
    public void GetDueReminders_DeadlineDateChangedSinceLastNotification_TreatedAsNewlyDue()
    {
        // A reminder already went out for the case's trial date as it stood a while ago (now stale
        // dedupe state). The trial date has since been pushed out, and the new date happens to be
        // exactly leadDays out today - it must fire again under its own (different) date key rather
        // than being suppressed by the stale entry.
        var staleNotifiedDate = Today.AddDays(-3);
        var currentTrialDate = Today.AddDays(7);
        var cases = new[] { new CaseDeadlineSnapshot(1, "24-CV-100", currentTrialDate, null) };
        var alreadyNotified = new HashSet<(long, string, DateOnly)> { (1, DeadlineReminderScanner.TrialDateType, staleNotifiedDate) };

        var due = DeadlineReminderScanner.GetDueReminders(cases, Today, 7, alreadyNotified);

        var reminder = Assert.Single(due);
        Assert.Equal(currentTrialDate, reminder.DeadlineDate);
    }

    [Fact]
    public void GetDueReminders_MultipleCases_OnlyTheDueOneIsReturned()
    {
        var cases = new[]
        {
            new CaseDeadlineSnapshot(1, "24-CV-100", Today.AddDays(7), null),
            new CaseDeadlineSnapshot(2, "24-CV-200", Today.AddDays(14), null),
        };

        var due = DeadlineReminderScanner.GetDueReminders(cases, Today, 7, NoneNotified);

        var reminder = Assert.Single(due);
        Assert.Equal(1, reminder.CaseId);
    }
}
