using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Tests;

public sealed class ActionableWorkQueryRulesTests
{
    private static readonly DateOnly Today = new(2026, 8, 1);

    [Fact]
    public void PipelineCasesRemainEligibleForOrdinaryWorkItems()
    {
        var record = new CaseRecord { CaseStatus = "Pipeline", Status = "Open" };

        Assert.True(ActionableWorkQueryRules.IsOpenCase(record));
        Assert.False(ActionableWorkQueryRules.IsDeferred(record, Today));
    }

    [Fact]
    public void DateWindowsAreInclusiveAndDoNotOverlap()
    {
        Assert.Equal("Overdue", ActionableWorkQueryRules.Classify(new DateOnly(2026, 7, 31), Today));
        Assert.Equal("Due Today", ActionableWorkQueryRules.Classify(Today, Today));
        Assert.Equal("Next 7 Days", ActionableWorkQueryRules.Classify(Today.AddDays(7), Today));
        Assert.Equal("Next 14 Days", ActionableWorkQueryRules.Classify(Today.AddDays(8), Today));
        Assert.Equal("Later", ActionableWorkQueryRules.Classify(Today.AddDays(31), Today));

        Assert.True(ActionableWorkQueryRules.IsOverdue(Today.AddDays(-1), Today));
        Assert.False(ActionableWorkQueryRules.IsOverdue(Today, Today));
        Assert.True(ActionableWorkQueryRules.IsDueInNextSevenDays(Today, Today));
        Assert.True(ActionableWorkQueryRules.IsDueInNextSevenDays(Today.AddDays(7), Today));
        Assert.False(ActionableWorkQueryRules.IsDueInNextSevenDays(Today.AddDays(8), Today));
    }

    [Fact]
    public void CompletedAndDeferredRecordsAreExcludedBySharedRules()
    {
        var deferred = new CaseRecord { CaseStatus = "Active Litigation", Status = "Open", DeferredUntil = "2026-08-02" };
        Assert.True(ActionableWorkQueryRules.IsDeferred(deferred, Today));
        Assert.False(ActionableWorkQueryRules.IsIncompleteChecklist(new ChecklistItemRecord { Status = "Done" }));
        Assert.False(ActionableWorkQueryRules.IsIncompleteDeadline(new DeadlineItem { Status = "Complete" }));
        Assert.False(ActionableWorkQueryRules.IsIncompleteDiscovery(new DiscoveryItemRecord { Status = "Complete" }));
    }
}
