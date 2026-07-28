using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Tests;

// Pre-filing sign-off/Settlement Authority final implementation, item 2 coverage: the unstructured,
// append-only review-note log. Mirrors SettlementAuthorityRequestTests's structure - a fresh
// RepositoryTestFixture per test, plain assertions against the real SQLite repository (no mocking).
public class ReviewNoteTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task<CaseRecord> CreateCaseAsync() =>
        await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Review Note Fixture Case",
            County = "Pulaski",
            Status = "Pipeline",
            Track = "Contested",
        });

    [Fact]
    public async Task CreateAsync_RoundTripsAllFields()
    {
        var c = await CreateCaseAsync();

        var created = await _fixture.Repository.CreateReviewNoteAsync(c.Id, new CreateReviewNoteRequest
        {
            ReviewerName = "Helen Newberry",
            ReviewerRole = "Deputy Chief Counsel",
            Decision = "Looks good",
            Comment = "Ready to file as-is.",
            OccurredDate = "2026-07-20",
        });

        Assert.NotEqual(0, created.Id);
        Assert.Equal(c.Id, created.CaseId);
        Assert.Equal("Helen Newberry", created.ReviewerName);
        Assert.Equal("Deputy Chief Counsel", created.ReviewerRole);
        Assert.Equal("Looks good", created.Decision);
        Assert.Equal("Ready to file as-is.", created.Comment);
        Assert.Equal("2026-07-20", created.OccurredDate);
        Assert.NotNull(created.CreatedAt);
        Assert.NotNull(created.CreatedByDisplay);

        var list = await _fixture.Repository.GetReviewNotesAsync(c.Id);
        var reloaded = Assert.Single(list);
        Assert.Equal("Looks good", reloaded.Decision);
    }

    [Fact]
    public async Task CreateAsync_ReviewerNameAndRoleAreFreeText_NotTiedToASystemUserOrFixedRole()
    {
        var c = await CreateCaseAsync();

        // The reviewer need not be any recognized manager_tier/role value, or even a system user -
        // per the norm that this review is a strong practice, not a gated workflow step.
        var created = await _fixture.Repository.CreateReviewNoteAsync(c.Id, new CreateReviewNoteRequest
        {
            ReviewerName = "Someone Filling In",
            ReviewerRole = "Contract Reviewing Attorney",
            Decision = "Other",
            Comment = "Ad hoc coverage while Deputy Chief Counsel was out.",
        });

        Assert.Equal("Someone Filling In", created.ReviewerName);
        Assert.Equal("Contract Reviewing Attorney", created.ReviewerRole);
    }

    [Fact]
    public async Task CreateAsync_NoOccurredDate_DefaultsToToday()
    {
        var c = await CreateCaseAsync();

        var created = await _fixture.Repository.CreateReviewNoteAsync(c.Id, new CreateReviewNoteRequest
        {
            Decision = "Looks good",
        });

        Assert.Equal(DateTime.UtcNow.ToString("yyyy-MM-dd"), created.OccurredDate);
    }

    [Fact]
    public async Task CreateAsync_ReviewerNameAndCommentAreOptional()
    {
        var c = await CreateCaseAsync();

        var created = await _fixture.Repository.CreateReviewNoteAsync(c.Id, new CreateReviewNoteRequest
        {
            Decision = "Sent back for revision",
        });

        Assert.Null(created.ReviewerName);
        Assert.Null(created.ReviewerRole);
        Assert.Null(created.Comment);
        Assert.Equal("Sent back for revision", created.Decision);
    }

    [Fact]
    public async Task CreateAsync_BlankDecision_ThrowsArgumentException()
    {
        var c = await CreateCaseAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _fixture.Repository.CreateReviewNoteAsync(c.Id, new CreateReviewNoteRequest { Decision = "   " }));

        Assert.Empty(await _fixture.Repository.GetReviewNotesAsync(c.Id));
    }

    [Fact]
    public async Task GetAsync_OrdersChronologicallyByOccurredDate_NotById()
    {
        var c = await CreateCaseAsync();

        // Inserted out of chronological order - proves the sort is by occurred_date, not insertion
        // order/id, matching this record type's "no fixed order" shape.
        var later = await _fixture.Repository.CreateReviewNoteAsync(c.Id, new CreateReviewNoteRequest { Decision = "Looks good", OccurredDate = "2026-07-25" });
        var earlier = await _fixture.Repository.CreateReviewNoteAsync(c.Id, new CreateReviewNoteRequest { Decision = "Sent back for revision", OccurredDate = "2026-07-10" });

        var list = await _fixture.Repository.GetReviewNotesAsync(c.Id);
        Assert.Equal(2, list.Count);
        Assert.Equal(earlier.Id, list[0].Id);
        Assert.Equal(later.Id, list[1].Id);
    }

    [Fact]
    public async Task CreateAsync_MultipleNotesOnSameCase_AllPersist_NoFixedOrderOrParticipantRequired()
    {
        var c = await CreateCaseAsync();

        await _fixture.Repository.CreateReviewNoteAsync(c.Id, new CreateReviewNoteRequest { Decision = "Looks good", ReviewerName = "First Reviewer" });
        await _fixture.Repository.CreateReviewNoteAsync(c.Id, new CreateReviewNoteRequest { Decision = "Sent back for revision", ReviewerName = "Second Reviewer" });
        await _fixture.Repository.CreateReviewNoteAsync(c.Id, new CreateReviewNoteRequest { Decision = "Looks good", ReviewerName = "First Reviewer" });

        var list = await _fixture.Repository.GetReviewNotesAsync(c.Id);
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public async Task CreateAsync_WritesReviewNoteAddedActivityLogEntry()
    {
        var c = await CreateCaseAsync();

        await _fixture.Repository.CreateReviewNoteAsync(c.Id, new CreateReviewNoteRequest
        {
            Decision = "Sent back for revision",
            Comment = "Legal description needs a revised metes-and-bounds.",
        });

        var log = await _fixture.Repository.GetActivityLogAsync(c.Id);
        var entry = Assert.Single(log, e => e.ActivityType == "ReviewNoteAdded");
        Assert.Contains("Sent back for revision", entry.Notes);
        Assert.Contains("metes-and-bounds", entry.Notes);
        Assert.True(entry.IsMeaningful);
    }

    [Fact]
    public async Task GetAsync_NullCaseId_ReturnsNotesAcrossAllCases()
    {
        var a = await CreateCaseAsync();
        var b = await _fixture.Repository.SaveCaseAsync(new CaseRecord { CaseName = "Second Fixture Case", County = "Pulaski", Status = "Pipeline", Track = "Contested" });

        await _fixture.Repository.CreateReviewNoteAsync(a.Id, new CreateReviewNoteRequest { Decision = "Looks good" });
        await _fixture.Repository.CreateReviewNoteAsync(b.Id, new CreateReviewNoteRequest { Decision = "Looks good" });

        var all = await _fixture.Repository.GetReviewNotesAsync(null);
        Assert.True(all.Count >= 2);
        Assert.Contains(all, n => n.CaseId == a.Id);
        Assert.Contains(all, n => n.CaseId == b.Id);
    }
}
