using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Tests;

public class AttorneyDashboardEngineTests
{
    private static readonly DateOnly Today = new(2026, 7, 10);

    private static CaseRecord MakeCase(Action<CaseRecord>? configure = null)
    {
        var c = new CaseRecord
        {
            Id = 1,
            CaseNumber = "TEST-0001",
            CaseName = "Test Case",
            JobNumber = "JOB-1",
            County = "Pulaski",
            Status = "Active",
            Stage = "Discovery & Evaluation",
            Track = "Contested",
            MatterType = "FiledCase",
        };
        configure?.Invoke(c);
        return c;
    }

    // ---------- 60-day inactivity / momentum ----------

    [Fact]
    public void DaysSinceMeaningfulActivity_ComputesWholeDayDifference()
    {
        var c = MakeCase(x => x.LastMeaningfulActivityDate = "2026-06-01");
        var days = AttorneyDashboardEngine.DaysSinceMeaningfulActivity(c, Today);
        Assert.Equal(39, days);
    }

    [Fact]
    public void DaysSinceMeaningfulActivity_FallsBackToLastActivityAt_WhenMeaningfulDateMissing()
    {
        var c = MakeCase(x =>
        {
            x.LastMeaningfulActivityDate = null;
            x.LastActivityAt = "2026-07-01T00:00:00Z";
        });
        var days = AttorneyDashboardEngine.DaysSinceMeaningfulActivity(c, Today);
        Assert.Equal(9, days);
    }

    [Fact]
    public void MomentumStatus_IsStalled_AtExactly60DaysWithNoWaitingRecord()
    {
        var days = 60;
        var status = AttorneyDashboardEngine.EvaluateMomentumStatus(MakeCase(), Today, days);
        Assert.Equal("Stalled", status);
    }

    [Fact]
    public void MomentumStatus_IsMoving_At59DaysWithNoWaitingRecord()
    {
        var status = AttorneyDashboardEngine.EvaluateMomentumStatus(MakeCase(), Today, 59);
        Assert.Equal("Moving", status);
    }

    [Fact]
    public void MomentumStatus_IsMoving_WhenRecentActivityEvenIfNoNextAction()
    {
        var status = AttorneyDashboardEngine.EvaluateMomentumStatus(MakeCase(), Today, 0);
        Assert.Equal("Moving", status);
    }

    // ---------- Waiting / follow-up logic ----------

    [Fact]
    public void MomentumStatus_WaitingAppropriately_WhenFollowUpDateNotYetArrived()
    {
        var c = MakeCase(x =>
        {
            x.WaitingOn = "Opposing counsel";
            x.WaitingFollowUpDate = "2026-08-01";
        });
        var status = AttorneyDashboardEngine.EvaluateMomentumStatus(c, Today, 200);
        Assert.Equal("Waiting Appropriately", status);
    }

    [Fact]
    public void MomentumStatus_ReviewRequired_WhenFollowUpDateHasPassed()
    {
        var c = MakeCase(x =>
        {
            x.WaitingOn = "Opposing counsel";
            x.WaitingFollowUpDate = "2026-07-01";
        });
        var status = AttorneyDashboardEngine.EvaluateMomentumStatus(c, Today, 200);
        Assert.Equal("Review Required", status);
    }

    [Fact]
    public void MomentumStatus_ReviewRequired_WhenWaitingRecordIncomplete_MissingFollowUpDate()
    {
        var c = MakeCase(x =>
        {
            x.WaitingOn = "Opposing counsel";
            x.WaitingFollowUpDate = null;
        });
        var status = AttorneyDashboardEngine.EvaluateMomentumStatus(c, Today, 200);
        Assert.Equal("Review Required", status);
    }

    [Fact]
    public void MomentumStatus_WaitingCaseNeverStalled_EvenAt200DaysInactive_IfFollowUpNotYetDue()
    {
        // Business rule: a waiting case does not become stalled until its follow-up date passes.
        var c = MakeCase(x =>
        {
            x.WaitingOn = "Owner's appraiser";
            x.WaitingFollowUpDate = "2027-01-01";
        });
        var status = AttorneyDashboardEngine.EvaluateMomentumStatus(c, Today, 500);
        Assert.Equal("Waiting Appropriately", status);
    }

    // ---------- Discovery strategy warnings ----------

    [Fact]
    public void DiscoveryConditions_FlagsStrategyNotSelected_WhenNoPosture()
    {
        var conditions = AttorneyDashboardEngine.EvaluateDiscoveryConditions(null, Today);
        Assert.Equal(["Strategy not selected"], conditions);
    }

    [Fact]
    public void DiscoveryConditions_FlagsStrategyNotSelected_WhenExplicitlyUnselected()
    {
        var posture = new DiscoveryPosture { Strategy = "Strategy not selected" };
        var conditions = AttorneyDashboardEngine.EvaluateDiscoveryConditions(posture, Today);
        Assert.Equal(["Strategy not selected"], conditions);
    }

    [Fact]
    public void DiscoveryConditions_FlagsNoDiscoveryNeeded_AsItsOwnBucket()
    {
        var posture = new DiscoveryPosture { Strategy = "No discovery currently needed" };
        var conditions = AttorneyDashboardEngine.EvaluateDiscoveryConditions(posture, Today);
        Assert.Equal(["No discovery currently needed"], conditions);
    }

    [Fact]
    public void DiscoveryConditions_FlagsNotServed_WhenStrategySelectedButNoServedDate()
    {
        var posture = new DiscoveryPosture { Strategy = "Written discovery first" };
        var conditions = AttorneyDashboardEngine.EvaluateDiscoveryConditions(posture, Today);
        Assert.Contains("Strategy selected but discovery not served", conditions);
    }

    [Fact]
    public void DiscoveryConditions_FlagsResponsesOverdue_PastDueDateWithNoResponse()
    {
        var posture = new DiscoveryPosture
        {
            Strategy = "Written discovery first",
            DiscoveryServedDate = "2026-06-01",
            ResponsesDueDate = "2026-07-01",
        };
        var conditions = AttorneyDashboardEngine.EvaluateDiscoveryConditions(posture, Today);
        Assert.Contains("Responses overdue", conditions);
    }

    [Fact]
    public void DiscoveryConditions_DoesNotFlagResponsesOverdue_OnceReceived()
    {
        var posture = new DiscoveryPosture
        {
            Strategy = "Written discovery first",
            DiscoveryServedDate = "2026-06-01",
            ResponsesDueDate = "2026-07-01",
            ResponsesReceivedDate = "2026-07-05",
        };
        var conditions = AttorneyDashboardEngine.EvaluateDiscoveryConditions(posture, Today);
        Assert.DoesNotContain("Responses overdue", conditions);
        Assert.Contains("Responses received but not reviewed", conditions);
    }

    [Fact]
    public void DiscoveryConditions_FlagsComplete_WhenIsCompleteTrue()
    {
        var posture = new DiscoveryPosture { Strategy = "Written discovery first", IsComplete = true };
        var conditions = AttorneyDashboardEngine.EvaluateDiscoveryConditions(posture, Today);
        Assert.Equal(["Discovery complete"], conditions);
    }

    [Fact]
    public void DiscoveryConditions_CanMatchMultipleSimultaneously_NotJustOneYesNoFlag()
    {
        var posture = new DiscoveryPosture
        {
            Strategy = "Written discovery first",
            DiscoveryServedDate = "2026-05-01",
            ResponsesReceivedDate = "2026-06-01",
            DeficiencyStatus = "Incomplete responses on drainage question",
            DiscoveryCutoffDate = "2026-07-20",
        };
        var conditions = AttorneyDashboardEngine.EvaluateDiscoveryConditions(posture, Today);
        Assert.Contains("Responses received but not reviewed", conditions);
        Assert.Contains("Deficiencies unresolved", conditions);
        Assert.Contains("Discovery cutoff approaching", conditions);
        Assert.True(conditions.Count >= 3);
    }

    // ---------- My Desk / pipeline Waiting logic ----------

    [Fact]
    public void PipelineBucket_IsMyDesk_WhenHolderIsAttorney()
    {
        var c = MakeCase(x => x.CurrentHolder = "Attorney");
        Assert.Equal("MyDesk", AttorneyDashboardEngine.PipelineBucket(c));
    }

    [Theory]
    [InlineData("Legal Assistant")]
    [InlineData("Deputy Chief Counsel")]
    [InlineData(null)]
    public void PipelineBucket_IsWaiting_ForAnyoneElse(string? holder)
    {
        var c = MakeCase(x => x.CurrentHolder = holder);
        Assert.Equal("Waiting", AttorneyDashboardEngine.PipelineBucket(c));
    }

    [Fact]
    public void PreFilingBelongsInActionQueue_True_WhenOnAttorneysDesk()
    {
        var c = MakeCase(x => x.CurrentHolder = "Attorney");
        Assert.True(AttorneyDashboardEngine.PreFilingBelongsInActionQueue(c, Today));
    }

    [Fact]
    public void PreFilingBelongsInActionQueue_False_WhenNormalPriorityWaitingAndFollowUpNotDue()
    {
        var c = MakeCase(x =>
        {
            x.CurrentHolder = "Legal Assistant";
            x.Priority = "Normal";
            x.WaitingFollowUpDate = "2026-08-01";
        });
        Assert.False(AttorneyDashboardEngine.PreFilingBelongsInActionQueue(c, Today));
    }

    [Fact]
    public void PreFilingBelongsInActionQueue_True_WhenRushed_EvenIfWaitingOnSomeoneElse()
    {
        var c = MakeCase(x =>
        {
            x.CurrentHolder = "Deputy Chief Counsel";
            x.Priority = "Rushed";
        });
        Assert.True(AttorneyDashboardEngine.PreFilingBelongsInActionQueue(c, Today));
    }

    [Fact]
    public void PreFilingBelongsInActionQueue_True_WhenFollowUpDateHasArrived()
    {
        var c = MakeCase(x =>
        {
            x.CurrentHolder = "Chief Counsel";
            x.Priority = "Normal";
            x.WaitingFollowUpDate = "2026-07-10";
        });
        Assert.True(AttorneyDashboardEngine.PreFilingBelongsInActionQueue(c, Today));
    }

    [Fact]
    public void WaitingMonitorReason_FlagsMissingHolderOrStage()
    {
        var c = MakeCase(x =>
        {
            x.CurrentHolder = null;
            x.PipelineStage = null;
            x.Priority = "Normal";
        });
        var reason = AttorneyDashboardEngine.WaitingMonitorReason(c, Today, 0);
        Assert.Equal("Missing current holder or stage", reason);
    }

    [Fact]
    public void WaitingMonitorReason_Null_ForOrdinaryWaitingRow_NothingToFlag()
    {
        var c = MakeCase(x =>
        {
            x.CurrentHolder = "Legal Assistant";
            x.PipelineStage = "With Legal Assistant";
            x.Priority = "Normal";
        });
        var reason = AttorneyDashboardEngine.WaitingMonitorReason(c, Today, 5);
        Assert.Null(reason);
    }

    [Fact]
    public void WaitingMonitorReason_FlagsStalledPipelineMovement_At60Days()
    {
        var c = MakeCase(x =>
        {
            x.CurrentHolder = "Deputy Chief Counsel";
            x.PipelineStage = "With Deputy Chief Counsel";
            x.Priority = "Normal";
        });
        var reason = AttorneyDashboardEngine.WaitingMonitorReason(c, Today, 60);
        Assert.Contains("No pipeline movement", reason);
    }

    // ---------- Trial-watch eligibility ----------

    [Fact]
    public void TrialWatch_Eligible_WhenTrialWithinWindow()
    {
        var c = MakeCase(x => x.TrialDate = "2026-09-01");
        Assert.True(AttorneyDashboardEngine.IsTrialWatchEligible(c, Today, 180));
    }

    [Fact]
    public void TrialWatch_NotEligible_WhenTrialBeyondWindow()
    {
        var c = MakeCase(x => x.TrialDate = "2028-01-01");
        Assert.False(AttorneyDashboardEngine.IsTrialWatchEligible(c, Today, 180));
    }

    [Fact]
    public void TrialWatch_Eligible_WhenManuallyMarkedTrialTrack_RegardlessOfTrialDate()
    {
        var c = MakeCase(x =>
        {
            x.TrialTrack = true;
            x.TrialDate = null;
        });
        Assert.True(AttorneyDashboardEngine.IsTrialWatchEligible(c, Today, 180));
    }

    [Fact]
    public void TrialWatch_NotEligible_ForOrdinaryCaseWithNoTrialDateOrFlag()
    {
        var c = MakeCase();
        Assert.False(AttorneyDashboardEngine.IsTrialWatchEligible(c, Today, 180));
    }

    [Fact]
    public void FeeComparisonNote_Null_WhenValuationsDoNotExceedThreshold()
    {
        var note = AttorneyDashboardEngine.BuildFeeComparisonNote(100_000m, 110_000m, null);
        Assert.Null(note);
    }

    [Fact]
    public void FeeComparisonNote_Present_WhenOwnerPositionExceeds20PercentOfDeposit()
    {
        var note = AttorneyDashboardEngine.BuildFeeComparisonNote(100_000m, 130_000m, null);
        Assert.NotNull(note);
        Assert.Contains("Trial consideration", note);
        Assert.DoesNotContain("risk", note, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- Warning consolidation ----------

    [Fact]
    public void EvaluateFiledCase_ConsolidatesMultipleSignals_IntoOneItemWithWarningCount()
    {
        var c = MakeCase(x =>
        {
            x.LastMeaningfulActivityDate = "2026-04-01"; // 100 days stalled
            x.NextAction = null;
            x.NextReviewDate = null;
        });
        var posture = new DiscoveryPosture { Strategy = "Strategy not selected" };
        var item = AttorneyDashboardEngine.EvaluateFiledCase(c, posture, [], [], Today);

        Assert.NotNull(item);
        // Both the discovery-strategy signal (priority 2) and the stalled-momentum signal
        // (priority 3) should have fired - consolidated into one queue entry, not two.
        Assert.True(item!.RelatedWarningCount >= 2);
        // Highest-priority (lowest number) signal wins as the displayed reason/category.
        Assert.Equal("Decide", item.ActionCategory);
        Assert.Equal(2, item.PriorityLevel);
    }

    [Fact]
    public void EvaluateFiledCase_ReturnsNull_ForAQuietHealthyCase()
    {
        var c = MakeCase(x =>
        {
            x.LastMeaningfulActivityDate = "2026-07-09";
            x.NextAction = "Await discovery responses";
            x.NextReviewDate = "2026-08-01";
        });
        var posture = new DiscoveryPosture { Strategy = "Written discovery first", DiscoveryServedDate = "2026-06-01" };
        var item = AttorneyDashboardEngine.EvaluateFiledCase(c, posture, [], [], Today);
        Assert.Null(item);
    }

    [Fact]
    public void EvaluateFiledCase_ReturnsNull_WhileCaseIsDeferred()
    {
        var c = MakeCase(x =>
        {
            x.DeferredUntil = "2026-08-01";
            x.LastMeaningfulActivityDate = "2025-01-01";
        });
        var overdue = new List<DeadlineItem>
        {
            new() { Id = 1, CaseId = c.Id, Title = "Overdue", DueDate = "2026-07-01", Status = "Open" }
        };

        Assert.Null(AttorneyDashboardEngine.EvaluateFiledCase(c, null, overdue, [], Today));
    }

    [Fact]
    public void EvaluateFiledCase_Priority1_ForMissedCourtDeadline()
    {
        var c = MakeCase();
        var deadlines = new List<DeadlineItem>
        {
            new() { Id = 1, CaseId = 1, Title = "Answer deadline", DueDate = "2026-07-01", Status = "Open" },
        };
        var item = AttorneyDashboardEngine.EvaluateFiledCase(c, null, deadlines, [], Today);
        Assert.NotNull(item);
        Assert.Equal(1, item!.PriorityLevel);
        Assert.Equal("Act", item.ActionCategory);
    }
}
