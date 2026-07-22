using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Tests;

// Integration coverage for the derived (not persisted) CaseRecord.DefaultPostureWarning field -
// confirms both read paths that stamp it via DefaultPostureCalculator (see
// CasePlannerRepository.ApplyCaseAttentionAsync, called from both GetCasesAsync and
// GetCaseWorkspaceAsync) actually produce the right value end to end against a real SQLite
// database, complementing DefaultPostureCalculatorTests's pure-function coverage of the rule
// itself.
public class DefaultPostureWarningTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task<CaseRecord> CreateCaseAsync(bool answerFiled, string? servicePerfectedDate) =>
        await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Fixture Case",
            County = "Pulaski",
            Status = "Active",
            Track = "Contested",
            ServiceRequired = true,
            ServicePerfected = servicePerfectedDate is not null,
            ServicePerfectedDate = servicePerfectedDate,
            AnswerFiled = answerFiled,
            AnswerFiledDate = answerFiled ? "2026-01-01" : null,
        });

    [Fact]
    public async Task GetCasesAsync_NoAnswerServicePerfectedOverThreshold_StampsWarning()
    {
        var stale = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-(DefaultPostureCalculator.NoAnswerThresholdDays + 10)).ToString("yyyy-MM-dd");
        var c = await CreateCaseAsync(answerFiled: false, servicePerfectedDate: stale);

        var cases = await _fixture.Repository.GetCasesAsync("", "", "", "", true);
        var found = Assert.Single(cases, x => x.Id == c.Id);

        Assert.True(found.DefaultPostureWarning);
    }

    [Fact]
    public async Task GetCasesAsync_AnswerFiled_NeverStampsWarningEvenWhenStale()
    {
        var stale = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-(DefaultPostureCalculator.NoAnswerThresholdDays + 10)).ToString("yyyy-MM-dd");
        var c = await CreateCaseAsync(answerFiled: true, servicePerfectedDate: stale);

        var cases = await _fixture.Repository.GetCasesAsync("", "", "", "", true);
        var found = Assert.Single(cases, x => x.Id == c.Id);

        Assert.False(found.DefaultPostureWarning);
    }

    [Fact]
    public async Task GetCasesAsync_NoAnswerButRecentService_DoesNotStampWarning()
    {
        var recent = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5).ToString("yyyy-MM-dd");
        var c = await CreateCaseAsync(answerFiled: false, servicePerfectedDate: recent);

        var cases = await _fixture.Repository.GetCasesAsync("", "", "", "", true);
        var found = Assert.Single(cases, x => x.Id == c.Id);

        Assert.False(found.DefaultPostureWarning);
    }

    [Fact]
    public async Task GetCaseWorkspaceAsync_MirrorsGetCasesAsyncDefaultPostureWarning()
    {
        var stale = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-(DefaultPostureCalculator.NoAnswerThresholdDays + 30)).ToString("yyyy-MM-dd");
        var c = await CreateCaseAsync(answerFiled: false, servicePerfectedDate: stale);

        var workspace = await _fixture.Repository.GetCaseWorkspaceAsync(c.Id);

        Assert.NotNull(workspace);
        Assert.True(workspace!.Case.DefaultPostureWarning);
    }
}
