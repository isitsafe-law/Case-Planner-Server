using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Services;

/// <summary>
/// Shared date and eligibility rules for Work Queue and dashboard work previews.
/// The dashboard may request fewer rows, but it must not invent a second definition
/// of actionable work.
/// </summary>
public static class ActionableWorkQueryRules
{
    public static bool IsOpenCase(CaseRecord record) =>
        record.CaseStatus is "Pipeline" or "Filed / Service Pending" or "Active Litigation" or "Settlement Pending" or "Trial Preparation"
        && record.Status is not ("Closed" or "Complete" or "Triage");

    public static bool IsDeferred(CaseRecord record, DateOnly today) =>
        DateOnly.TryParse(record.DeferredUntil, out var deferred) && deferred > today;

    public static bool IsIncompleteChecklist(ChecklistItemRecord item) =>
        item.Status is not ("Done" or "Complete" or "N/A");

    public static bool IsIncompleteDeadline(DeadlineItem item) =>
        item.Status is not ("Done" or "Complete");

    public static bool IsIncompleteDiscovery(DiscoveryItemRecord item) =>
        !item.Status.Contains("complete", StringComparison.OrdinalIgnoreCase)
        && !item.Status.Contains("cancel", StringComparison.OrdinalIgnoreCase);

    public static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParse(value, out var date) && date != new DateOnly(1900, 1, 1) ? date : null;

    public static string Classify(DateOnly? due, DateOnly today) =>
        due is null ? "No Due Date"
        : due.Value < today ? "Overdue"
        : due.Value == today ? "Due Today"
        : due.Value <= today.AddDays(7) ? "Next 7 Days"
        : due.Value <= today.AddDays(14) ? "Next 14 Days"
        : due.Value <= today.AddDays(30) ? "Next 30 Days"
        : "Later";

    public static bool MatchesUrgency(string requested, string actual, DateOnly? due, DateOnly today) =>
        requested is "" or "All Open"
        || requested == actual
        || requested == "Next 7 Days" && due is { } value7 && value7 >= today && value7 <= today.AddDays(7)
        || requested == "Next 14 Days" && due is { } value14 && value14 >= today && value14 <= today.AddDays(14)
        || requested == "Next 30 Days" && due is { } value30 && value30 >= today && value30 <= today.AddDays(30);

    public static bool IsDueInNextSevenDays(DateOnly? due, DateOnly today) =>
        due is { } value && value >= today && value <= today.AddDays(7);

    public static bool IsOverdue(DateOnly? due, DateOnly today) => due is { } value && value < today;
}
