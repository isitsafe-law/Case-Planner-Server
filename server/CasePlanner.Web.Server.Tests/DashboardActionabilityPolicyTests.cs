using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Tests;

public sealed class DashboardActionabilityPolicyTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task PolicyDefaultsAreAvailableAndPersisted()
    {
        var defaults = await _fixture.Repository.GetDashboardActionabilityPolicyAsync();

        Assert.Equal(60, defaults.MomentumStaleDays);
        Assert.Equal(60, defaults.PipelineStalledDays);
        Assert.Equal(45, defaults.DiscoveryCutoffLookaheadDays);
        Assert.Equal(60, defaults.TrialPreparationLookaheadDays);
        Assert.Equal(180, defaults.TrialWatchLookaheadDays);

        var saved = await _fixture.Repository.SaveDashboardActionabilityPolicyAsync(new SaveDashboardActionabilityPolicyRequest
        {
            MomentumStaleDays = 30,
            PipelineStalledDays = 45,
            DiscoveryCutoffLookaheadDays = 21,
            TrialPreparationLookaheadDays = 90,
            TrialWatchLookaheadDays = 240,
        });

        var reloaded = await _fixture.Repository.GetDashboardActionabilityPolicyAsync();
        Assert.Equal(30, saved.MomentumStaleDays);
        Assert.Equal(45, reloaded.PipelineStalledDays);
        Assert.Equal(21, reloaded.DiscoveryCutoffLookaheadDays);
        Assert.Equal(90, reloaded.TrialPreparationLookaheadDays);
        Assert.Equal(240, reloaded.TrialWatchLookaheadDays);
    }

    [Fact]
    public async Task PolicyRejectsUnsafeThresholds()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _fixture.Repository.SaveDashboardActionabilityPolicyAsync(new SaveDashboardActionabilityPolicyRequest
        {
            MomentumStaleDays = 0,
            PipelineStalledDays = 60,
            DiscoveryCutoffLookaheadDays = 45,
            TrialPreparationLookaheadDays = 60,
            TrialWatchLookaheadDays = 180,
        }));
    }

    [Theory]
    [InlineData("deadline", -2, "Deadline is overdue by 2 days.", "Fixed legal/operational due date")]
    [InlineData("task", 0, "Task is due today.", "Recorded task due date")]
    [InlineData("discovery", 5, "Discovery follow-up is due in 5 days.", "Recorded discovery follow-up or response date")]
    public void UpcomingWorkExplanationUsesSharedDateRules(string itemType, int offset, string expectedWhy, string expectedThreshold)
    {
        var today = new DateOnly(2026, 8, 1);
        var result = ActionableWorkQueryRules.ExplainUpcomingWork(itemType, today.AddDays(offset), today);

        Assert.Equal(expectedWhy, result.Why);
        Assert.Equal(expectedThreshold, result.Threshold);
    }
}
