using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Services;

// Computes the condemnation-litigation Attorney Dashboard: action-queue priority/category,
// discovery conditions, momentum status, pre-filing pipeline classification, and trial-watch
// eligibility. Deliberately a third, independent layer alongside CaseAttentionEngine (Cases list
// attention pill) and DashboardTriageEngine (the old Dashboard tab, retired once this ships) -
// same "purely additive, must not change the others' behavior" precedent those two already set.
public static class AttorneyDashboardEngine
{
    public const int MomentumStaleDays = 60;
    public const int DefaultTrialWatchDays = 180;
    public const int DiscoveryCutoffLookaheadDays = 45;
    public const int TrialPrepLookaheadDays = 60;
    public const int PipelineStalledDays = 60;

    private static readonly HashSet<string> NoStrategyNeededValues = new(StringComparer.Ordinal)
    {
        "No discovery currently needed",
    };

    private static readonly HashSet<string> UnselectedStrategyValues = new(StringComparer.Ordinal)
    {
        "", "Strategy not selected",
    };

    // Priority 1 (Immediate) .. 4 (Planned Work), lower sorts first.
    private const int Priority1Immediate = 1;
    private const int Priority2Decision = 2;
    private const int Priority3Momentum = 3;
    private const int Priority4Planned = 4;

    private sealed record Signal(int Priority, string ActionCategory, string Reason, string NextAction, string? ReviewDate, long? RelatedDeadlineId = null);

    // ---------- Momentum ----------

    public static string EvaluateMomentumStatus(CaseRecord c, DateOnly today, int? daysSinceMeaningfulActivity)
    {
        var hasWaitingRecord = !string.IsNullOrWhiteSpace(c.WaitingOn);
        if (hasWaitingRecord)
        {
            // Rule: a waiting case does not become stalled until its follow-up date passes or its
            // waiting record is incomplete (missing the follow-up date itself).
            if (!DateOnly.TryParse(c.WaitingFollowUpDate, out var followUp))
            {
                return "Review Required";
            }

            return followUp >= today ? "Waiting Appropriately" : "Review Required";
        }

        if (daysSinceMeaningfulActivity is { } days && days >= MomentumStaleDays)
        {
            return "Stalled";
        }

        return "Moving";
    }

    public static int? DaysSinceMeaningfulActivity(CaseRecord c, DateOnly today)
    {
        var source = c.LastMeaningfulActivityDate ?? c.LastActivityAt;
        if (string.IsNullOrEmpty(source) || source.Length < 10 || !DateOnly.TryParse(source[..10], out var lastDate))
        {
            return null;
        }

        return Math.Max(0, today.DayNumber - lastDate.DayNumber);
    }

    // ---------- Discovery Control ----------

    public static List<string> EvaluateDiscoveryConditions(DiscoveryPosture? posture, DateOnly today)
    {
        var conditions = new List<string>();
        var strategy = posture?.Strategy ?? "Strategy not selected";

        if (UnselectedStrategyValues.Contains(strategy))
        {
            conditions.Add("Strategy not selected");
            return conditions;
        }

        if (NoStrategyNeededValues.Contains(strategy))
        {
            conditions.Add("No discovery currently needed");
            return conditions;
        }

        if (posture is null)
        {
            return conditions;
        }

        if (posture.IsComplete)
        {
            conditions.Add("Discovery complete");
            return conditions;
        }

        if (string.IsNullOrWhiteSpace(posture.DiscoveryServedDate))
        {
            conditions.Add("Strategy selected but discovery not served");
        }

        if (DateOnly.TryParse(posture.ResponsesDueDate, out var due) && due < today && string.IsNullOrWhiteSpace(posture.ResponsesReceivedDate))
        {
            conditions.Add("Responses overdue");
        }

        if (!string.IsNullOrWhiteSpace(posture.ResponsesReceivedDate) && string.IsNullOrWhiteSpace(posture.ResponsesReviewedDate))
        {
            conditions.Add("Responses received but not reviewed");
        }

        if (!string.IsNullOrWhiteSpace(posture.DeficiencyStatus) && !posture.DeficiencyStatus.Equals("None", StringComparison.OrdinalIgnoreCase)
            && !posture.DeficiencyStatus.Equals("Resolved", StringComparison.OrdinalIgnoreCase))
        {
            conditions.Add("Deficiencies unresolved");
        }

        if ((strategy == "Landowner deposition first" || strategy == "Appraiser discovery first")
            && string.IsNullOrWhiteSpace(posture.PlannedDepositions))
        {
            conditions.Add("Deposition decision pending");
        }

        if (DateOnly.TryParse(posture.DiscoveryCutoffDate, out var cutoff))
        {
            var daysOut = cutoff.DayNumber - today.DayNumber;
            if (daysOut <= DiscoveryCutoffLookaheadDays)
            {
                conditions.Add("Discovery cutoff approaching");
            }
        }

        if (conditions.Count == 0)
        {
            conditions.Add("No discovery currently needed");
        }

        return conditions;
    }

    // ---------- Trial Watch ----------

    public static bool IsTrialWatchEligible(CaseRecord c, DateOnly today, int trialWatchDays)
    {
        if (c.TrialTrack)
        {
            return true;
        }

        if (DateOnly.TryParse(c.TrialDate, out var trial))
        {
            var daysOut = trial.DayNumber - today.DayNumber;
            return daysOut >= 0 && daysOut <= trialWatchDays;
        }

        return false;
    }

    // Neutral, non-alarmist wording per the dashboard brief - never a generic valuation-gap
    // warning, only shown when the case is already trial-watch eligible.
    public static string? BuildFeeComparisonNote(decimal? deposit, decimal? ownerAppraisal, decimal? ownerDemand)
    {
        var highWaterMark = new[] { ownerAppraisal, ownerDemand }.Where(v => v.HasValue).Select(v => v!.Value).DefaultIfEmpty(0m).Max();
        if (deposit is not { } depositValue || depositValue <= 0 || highWaterMark <= depositValue * 1.2m)
        {
            return null;
        }

        return "Trial consideration: Current valuation positions exceed the statutory fee-comparison point. Review before final trial valuation and settlement recommendations.";
    }

    // ---------- Filed-case Action Queue ----------

    public static ActionQueueItem? EvaluateFiledCase(
        CaseRecord c,
        DiscoveryPosture? posture,
        IReadOnlyList<DeadlineItem> openDeadlines,
        IReadOnlyList<HearingRecord> hearings,
        DateOnly today)
    {
        if (DateOnly.TryParse(c.DeferredUntil, out var deferredUntil) && deferredUntil > today)
        {
            return null;
        }

        var signals = new List<Signal>();
        var daysSince = DaysSinceMeaningfulActivity(c, today);
        var momentum = EvaluateMomentumStatus(c, today, daysSince);

        AddOverdueDeadlineSignal(signals, openDeadlines, today);
        AddCourtEventSignal(signals, c, hearings, today);
        if (posture?.IsComplete != true)
        {
            AddDiscoveryStrategySignal(signals, posture);
            AddDiscoveryCutoffSignal(signals, posture, today);
            AddDiscoveryOtherSignals(signals, posture, today);
        }
        AddMomentumSignal(signals, c, momentum, daysSince, today);
        AddMissingReviewSignal(signals, c, momentum);
        AddTrialPrepSignal(signals, c, today);

        if (signals.Count == 0)
        {
            return null;
        }

        var primary = signals.OrderBy(s => s.Priority).First();
        return new ActionQueueItem
        {
            CaseId = c.Id,
            CaseName = c.CaseName,
            CaseNumber = string.IsNullOrWhiteSpace(c.CaseNumber) ? null : c.CaseNumber,
            JobNumber = string.IsNullOrWhiteSpace(c.JobNumber) ? null : c.JobNumber,
            CurrentPhase = c.Stage,
            ActionCategory = primary.ActionCategory,
            PriorityLevel = primary.Priority,
            Reason = primary.Reason,
            PostureSummary = string.IsNullOrWhiteSpace(c.ShortPostureSummary) ? primary.Reason : c.ShortPostureSummary,
            RecommendedNextAction = primary.NextAction,
            ReviewDate = primary.ReviewDate ?? c.NextReviewDate ?? c.NextActionDue,
            DaysSinceMeaningfulActivity = daysSince,
            RelatedWarningCount = signals.Count,
            CurrentHolder = c.CurrentHolder,
            MatterType = "FiledCase",
            RelatedDeadlineId = primary.RelatedDeadlineId,
        };
    }

    private static void AddOverdueDeadlineSignal(List<Signal> signals, IReadOnlyList<DeadlineItem> openDeadlines, DateOnly today)
    {
        var overdue = openDeadlines
            .Where(d => DateOnly.TryParse(d.DueDate, out var due) && due <= today)
            .OrderBy(d => d.DueDate, StringComparer.Ordinal)
            .FirstOrDefault();
        if (overdue is null)
        {
            return;
        }

        signals.Add(new Signal(Priority1Immediate, "Act", $"Missed court deadline: {overdue.Title}", "Resolve the missed deadline immediately", overdue.DueDate, overdue.Id));
    }

    private static void AddCourtEventSignal(List<Signal> signals, CaseRecord c, IReadOnlyList<HearingRecord> hearings, DateOnly today)
    {
        DateOnly? soonest = null;
        string? label = null;
        if (DateOnly.TryParse(c.TrialDate, out var trial) && trial >= today && trial <= today.AddDays(TrialPrepLookaheadDays))
        {
            soonest = trial;
            label = "Trial";
        }

        foreach (var hearing in hearings)
        {
            if (DateOnly.TryParse(hearing.HearingDate, out var hearingDate) && hearingDate >= today
                && hearingDate <= today.AddDays(TrialPrepLookaheadDays) && (soonest is null || hearingDate < soonest))
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
        var priority = daysOut <= 14 ? Priority1Immediate : Priority4Planned;
        signals.Add(new Signal(priority, "Prepare", $"{label} approaching in {daysOut} day{(daysOut == 1 ? "" : "s")}",
            "Confirm trial-preparation readiness (witnesses, exhibits, discovery status)", eventDate.ToString("yyyy-MM-dd")));
    }

    private static void AddDiscoveryStrategySignal(List<Signal> signals, DiscoveryPosture? posture)
    {
        var strategy = posture?.Strategy ?? "Strategy not selected";
        if (UnselectedStrategyValues.Contains(strategy))
        {
            signals.Add(new Signal(Priority2Decision, "Decide", "Discovery strategy not selected", "Select a discovery strategy for this case", posture?.NextReviewDate));
        }
    }

    private static void AddDiscoveryCutoffSignal(List<Signal> signals, DiscoveryPosture? posture, DateOnly today)
    {
        if (posture is null || posture.IsComplete || !DateOnly.TryParse(posture.DiscoveryCutoffDate, out var cutoff))
        {
            return;
        }

        var daysOut = cutoff.DayNumber - today.DayNumber;
        if (daysOut < 0)
        {
            signals.Add(new Signal(Priority1Immediate, "Escalate", "Discovery cutoff has passed with the current plan incomplete", "Resolve outstanding discovery before the cutoff issue compounds", posture.DiscoveryCutoffDate));
        }
        else if (daysOut <= DiscoveryCutoffLookaheadDays)
        {
            signals.Add(new Signal(Priority1Immediate, "Act", $"Discovery cutoff in {daysOut} day{(daysOut == 1 ? "" : "s")} threatens the current plan", "Complete or accelerate remaining discovery before the cutoff", posture.DiscoveryCutoffDate));
        }
    }

    private static void AddDiscoveryOtherSignals(List<Signal> signals, DiscoveryPosture? posture, DateOnly today)
    {
        if (posture is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(posture.ResponsesReceivedDate) && string.IsNullOrWhiteSpace(posture.ResponsesReviewedDate))
        {
            signals.Add(new Signal(Priority2Decision, "Review", "Discovery responses received but not yet substantively reviewed", "Review discovery responses and record findings", posture.NextReviewDate));
        }

        if (!string.IsNullOrWhiteSpace(posture.DeficiencyStatus)
            && !posture.DeficiencyStatus.Equals("None", StringComparison.OrdinalIgnoreCase)
            && !posture.DeficiencyStatus.Equals("Resolved", StringComparison.OrdinalIgnoreCase))
        {
            signals.Add(new Signal(Priority2Decision, "Act", $"Discovery deficiency unresolved: {posture.DeficiencyStatus}", "Decide whether to send a good-faith letter or move to compel", posture.NextReviewDate));
        }
    }

    private static void AddMomentumSignal(List<Signal> signals, CaseRecord c, string momentum, int? daysSince, DateOnly today)
    {
        if (momentum == "Stalled")
        {
            signals.Add(new Signal(Priority3Momentum, "Review", $"No meaningful litigation activity for {daysSince} days", "Review case status and record a decision or next step", c.NextReviewDate));
        }
        else if (momentum == "Review Required")
        {
            var incomplete = string.IsNullOrWhiteSpace(c.WaitingFollowUpDate);
            var reason = incomplete
                ? "Waiting record is missing a follow-up date"
                : $"Waiting follow-up date ({c.WaitingFollowUpDate}) has passed with no response";
            signals.Add(new Signal(Priority3Momentum, "Escalate", reason,
                incomplete ? "Set a follow-up date for this waiting condition" : (c.WaitingEscalationAction ?? "Follow up with the party this case is waiting on"),
                c.WaitingFollowUpDate));
        }
    }

    private static void AddMissingReviewSignal(List<Signal> signals, CaseRecord c, string momentum)
    {
        // Business rule: every active filed matter needs a next action + review date, OR a
        // documented waiting condition, OR a dated reason no action is needed. If none of the
        // three exist, that gap itself is the signal (separate from - and lower priority than -
        // an already-stalled case, which AddMomentumSignal already covers).
        var hasNextAction = !string.IsNullOrWhiteSpace(c.NextAction) && !string.IsNullOrWhiteSpace(c.NextReviewDate ?? c.NextActionDue);
        var hasWaiting = !string.IsNullOrWhiteSpace(c.WaitingOn);
        if (!hasNextAction && !hasWaiting && momentum != "Stalled")
        {
            signals.Add(new Signal(Priority3Momentum, "Decide", "No next action or review date set", "Set a next attorney action and review date, or document why none is needed", null));
        }
    }

    private static void AddTrialPrepSignal(List<Signal> signals, CaseRecord c, DateOnly today)
    {
        if (!c.TrialTrack || !DateOnly.TryParse(c.TrialDate, out var trial))
        {
            return;
        }

        var daysOut = trial.DayNumber - today.DayNumber;
        if (daysOut is >= 0 and <= TrialPrepLookaheadDays)
        {
            signals.Add(new Signal(Priority4Planned, "Prepare", $"Trial in {daysOut} day{(daysOut == 1 ? "" : "s")} - preparation milestone window", "Confirm witness list, exhibit list, and final valuation position", trial.ToString("yyyy-MM-dd")));
        }
    }

    // ---------- Pre-filing pipeline ----------

    public static string PipelineBucket(CaseRecord c)
    {
        return string.Equals(c.CurrentHolder, "Attorney", StringComparison.OrdinalIgnoreCase) ? "MyDesk" : "Waiting";
    }

    // Reason a Waiting tract should still surface for monitoring (spec's explicit exception
    // list), or null for a plain "waiting, nothing to see" row that stays off the action queue.
    public static string? WaitingMonitorReason(CaseRecord c, DateOnly today, int? daysSincePipelineMovement)
    {
        if (DateOnly.TryParse(c.WaitingFollowUpDate, out var followUp) && followUp <= today)
        {
            return "Follow-up date has arrived";
        }

        if (c.Priority is "Priority" or "Rushed")
        {
            return $"Marked {c.Priority}";
        }

        if (string.IsNullOrWhiteSpace(c.CurrentHolder) || string.IsNullOrWhiteSpace(c.PipelineStage))
        {
            return "Missing current holder or stage";
        }

        if (daysSincePipelineMovement is { } days && days >= PipelineStalledDays)
        {
            return $"No pipeline movement for {days} days - needs a general status review";
        }

        return null;
    }

    public static string? MyDeskFlagReason(CaseRecord c)
    {
        if (string.Equals(c.PipelineStage, "Returned for Revision", StringComparison.OrdinalIgnoreCase))
        {
            return "Returned by a senior attorney for revision";
        }

        if (c.Priority is "Priority" or "Rushed")
        {
            return $"Marked {c.Priority}";
        }

        return "Attorney review required";
    }

    // Whether a pre-filing tract belongs in the main Attorney Action Queue at all - the spec is
    // explicit that most pre-filing tracts (normal-priority, waiting on someone else, before
    // their follow-up date) should NOT show up there.
    public static bool PreFilingBelongsInActionQueue(CaseRecord c, DateOnly today)
    {
        if (string.Equals(c.CurrentHolder, "Attorney", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (c.Priority is "Priority" or "Rushed")
        {
            return true;
        }

        return DateOnly.TryParse(c.WaitingFollowUpDate, out var followUp) && followUp <= today;
    }

    public static ActionQueueItem EvaluatePreFilingTract(CaseRecord c, DateOnly today)
    {
        var onDesk = string.Equals(c.CurrentHolder, "Attorney", StringComparison.OrdinalIgnoreCase);
        var reason = onDesk
            ? MyDeskFlagReason(c)!
            : (DateOnly.TryParse(c.WaitingFollowUpDate, out var followUp) && followUp <= today
                ? $"Follow-up date ({c.WaitingFollowUpDate}) has arrived"
                : $"Marked {c.Priority}");
        var category = onDesk ? "Decide" : "Review";
        var priority = c.Priority == "Rushed" ? Priority1Immediate : Priority2Decision;

        return new ActionQueueItem
        {
            CaseId = c.Id,
            CaseName = c.CaseName,
            CaseNumber = string.IsNullOrWhiteSpace(c.CaseNumber) ? null : c.CaseNumber,
            JobNumber = string.IsNullOrWhiteSpace(c.JobNumber) ? null : c.JobNumber,
            CurrentPhase = c.PipelineStage ?? "Pipeline stage not set",
            ActionCategory = category,
            PriorityLevel = priority,
            Reason = reason,
            PostureSummary = string.IsNullOrWhiteSpace(c.ShortPostureSummary)
                ? (string.IsNullOrWhiteSpace(c.CurrentIssue) ? "No current issue documented" : c.CurrentIssue)
                : c.ShortPostureSummary,
            RecommendedNextAction = onDesk ? "Review the pleadings and right-of-way file" : "Confirm status with the current holder",
            ReviewDate = c.NextReviewDate,
            DaysSinceMeaningfulActivity = DaysSinceMeaningfulActivity(c, today),
            RelatedWarningCount = 1,
            CurrentHolder = c.CurrentHolder,
            MatterType = "PreFilingTract",
        };
    }
}
