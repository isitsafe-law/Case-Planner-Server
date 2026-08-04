using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Tests;

// Covers RecordTitleReviewRoundAsync (case_prefiling_review_events, event_type="TitleReview"): a
// separate action space from RecordPrefilingReviewActionAsync's internal holder-chain actions,
// used to track a ROW-intake tract's earlier, orthogonal stage (see CaseRecord.RowIntakeStatus).
public sealed class RowTitleReviewTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task<CaseRecord> CreateRowIntakeCaseAsync() => await _fixture.Repository.SaveCaseAsync(new CaseRecord
    {
        CaseName = "ROW Intake Tract",
        County = "Pulaski",
        Status = "Active",
        CaseStatus = "Pipeline",
        MatterType = "PreFilingTract",
        RowIntakeStatus = "Received from ROW",
        Track = "Contested",
    });

    [Fact]
    public async Task SaveCaseAsync_RoundTripsRowIntakeStatus_WithoutAnyTitleReviewRound()
    {
        var c = await CreateRowIntakeCaseAsync();
        Assert.Equal("Received from ROW", c.RowIntakeStatus);
        Assert.Equal("Pipeline", c.CaseStatus); // stays Pipeline even though PipelineStage isn't set yet

        var reloaded = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == c.Id);
        Assert.Equal("Received from ROW", reloaded.RowIntakeStatus);
    }

    [Fact]
    public async Task RecordTitleReviewRoundAsync_TransitionsRowIntakeStatus_AndAppendsEvent()
    {
        var c = await CreateRowIntakeCaseAsync();

        var recorded = await _fixture.Repository.RecordTitleReviewRoundAsync(c.Id, new TitleReviewRoundRequest
        {
            Outcome = "In Title Review",
            ReviewerDisplay = "Jane Title Attorney",
            Note = "Pulled title, opened review.",
        });

        Assert.Equal("TitleReview", recorded.EventType);
        Assert.Equal("In Title Review", recorded.Outcome);
        Assert.Equal("Jane Title Attorney", recorded.ReviewerDisplay);

        var updated = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == c.Id);
        Assert.Equal("In Title Review", updated.RowIntakeStatus);
        Assert.Equal("Pipeline", updated.CaseStatus); // stays Pipeline through the whole pre-filing lifecycle

        var events = await _fixture.Repository.GetPrefilingReviewEventsAsync(c.Id);
        var titleEvent = Assert.Single(events, e => e.EventType == "TitleReview");
        Assert.Equal("In Title Review", titleEvent.Outcome);
        Assert.Equal("Jane Title Attorney", titleEvent.ReviewerDisplay);
    }

    [Fact]
    public async Task RecordTitleReviewRoundAsync_MultipleRounds_AccumulateInOrder()
    {
        var c = await CreateRowIntakeCaseAsync();

        await _fixture.Repository.RecordTitleReviewRoundAsync(c.Id, new TitleReviewRoundRequest
        {
            Outcome = "In Title Review", ReviewerDisplay = "Jane Title Attorney",
        });
        await _fixture.Repository.RecordTitleReviewRoundAsync(c.Id, new TitleReviewRoundRequest
        {
            Outcome = "Returned to ROW", ReviewerDisplay = "Jane Title Attorney", Note = "Legal description mismatch.",
        });
        await _fixture.Repository.RecordTitleReviewRoundAsync(c.Id, new TitleReviewRoundRequest
        {
            Outcome = "Ready for Assignment", ReviewerDisplay = "Jane Title Attorney",
        });

        var updated = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == c.Id);
        Assert.Equal("Ready for Assignment", updated.RowIntakeStatus);

        var rounds = (await _fixture.Repository.GetPrefilingReviewEventsAsync(c.Id))
            .Where(e => e.EventType == "TitleReview")
            .OrderBy(e => e.Id)
            .Select(e => e.Outcome)
            .ToList();
        Assert.Equal(["In Title Review", "Returned to ROW", "Ready for Assignment"], rounds);
    }

    [Fact]
    public async Task RecordTitleReviewRoundAsync_RejectsUnsupportedOutcome()
    {
        var c = await CreateRowIntakeCaseAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => _fixture.Repository.RecordTitleReviewRoundAsync(c.Id, new TitleReviewRoundRequest
        {
            Outcome = "Not A Real Status",
            ReviewerDisplay = "Jane Title Attorney",
        }));
    }

    [Fact]
    public async Task RecordTitleReviewRoundAsync_RequiresReviewerDisplay()
    {
        var c = await CreateRowIntakeCaseAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => _fixture.Repository.RecordTitleReviewRoundAsync(c.Id, new TitleReviewRoundRequest
        {
            Outcome = "In Title Review",
            ReviewerDisplay = "   ",
        }));
    }

    [Fact]
    public async Task RecordTitleReviewRoundAsync_UnknownCase_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _fixture.Repository.RecordTitleReviewRoundAsync(999_999, new TitleReviewRoundRequest
        {
            Outcome = "In Title Review",
            ReviewerDisplay = "Jane Title Attorney",
        }));
    }
}
