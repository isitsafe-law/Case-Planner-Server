using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Tests;

public sealed class RiskAnalysisHistoryTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;
    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task SaveRetainsHistoryAndUsesLastQualifyingScenarioAsKeyScenario()
    {
        var first = await _fixture.Repository.SaveRiskAnalysisAsync(new RiskAnalysisInput
        {
            CaseId = 1,
            AnalysisDate = "2026-07-12",
            Rows = [
                new RiskAnalysisRowInput { RowKey = "LandownerOpinionOfValue", Label = "LANDOWNER OPINION", JustCompensation = 100000 },
                new RiskAnalysisRowInput { RowKey = "AshcFirstOffer", Label = "ASHC FIRST OFFER", JustCompensation = 90000 },
            ]
        });
        await _fixture.Repository.SaveRiskAnalysisAsync(new RiskAnalysisInput
        {
            CaseId = 1,
            AnalysisDate = "2026-07-13",
            Rows = [
                new RiskAnalysisRowInput { RowKey = "LandownerOpinionOfValue", Label = "LANDOWNER OPINION", JustCompensation = 100000 },
                new RiskAnalysisRowInput { RowKey = "AshcFirstOffer", Label = "ASHC FIRST OFFER", JustCompensation = 90000 },
                new RiskAnalysisRowInput { RowKey = "LandownerCounteroffer", Label = "LANDOWNER COUNTEROFFER", JustCompensation = 120000 },
            ]
        });

        var history = await _fixture.Repository.GetRiskAnalysisHistoryAsync(1);
        Assert.Equal(2, history.Count);
        Assert.Equal("LANDOWNER COUNTEROFFER", history[0].KeyScenarioLabel);
        Assert.Equal(120000, history[0].KeyScenarioValue);
        Assert.Equal("ASHC FIRST OFFER", history[1].KeyScenarioLabel);
        Assert.NotEqual(first.Id, history[0].Id);
    }

    [Fact]
    public async Task DeleteHistoryRemovesOnlySelectedSnapshot()
    {
        await _fixture.Repository.SaveRiskAnalysisAsync(new RiskAnalysisInput { CaseId = 1, Rows = [new RiskAnalysisRowInput { RowKey = "AshcFirstOffer", Label = "ASHC FIRST OFFER", JustCompensation = 90000 }] });
        await _fixture.Repository.SaveRiskAnalysisAsync(new RiskAnalysisInput { CaseId = 1, Rows = [new RiskAnalysisRowInput { RowKey = "LandownerCounteroffer", Label = "LANDOWNER COUNTEROFFER", JustCompensation = 120000 }] });
        var history = await _fixture.Repository.GetRiskAnalysisHistoryAsync(1);
        await _fixture.Repository.DeleteRiskAnalysisHistoryAsync(1, history[0].Id);
        var remaining = await _fixture.Repository.GetRiskAnalysisHistoryAsync(1);
        Assert.Single(remaining);
        Assert.Equal(history[1].Id, remaining[0].Id);
    }
}
