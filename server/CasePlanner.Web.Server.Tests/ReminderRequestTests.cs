using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Tests;

// Covers RequestOrFollowUpAttorneyReminderAsync/ResolveReminderAsync/GetOpenAttorneyRemindersAsync
// (Legal Assistant Dashboard audit Phase 4 - see ReminderRequestRecord's doc comment). The core rule
// under test: repeated reminders on a still-open thread append history rather than opening a second
// thread, so the Action Queue never shows duplicates for the same ask.
public sealed class ReminderRequestTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task<CaseRecord> CreateCaseAsync() => await _fixture.Repository.SaveCaseAsync(new CaseRecord
    {
        CaseName = "Reminder Test Case",
        County = "Pulaski",
        Status = "Active",
        CaseStatus = "Active Litigation",
        AssignedAttorney = "Sample Attorney",
        Track = "Contested",
    });

    [Fact]
    public async Task RequestOrFollowUpAsync_FirstCall_CreatesRequestedThread()
    {
        var c = await CreateCaseAsync();
        var result = await _fixture.Repository.RequestOrFollowUpAttorneyReminderAsync(c.Id, new RequestAttorneyReminderRequest
        {
            RequestedAction = "Review discovery responses",
            TargetAttorneyDisplay = "Sample Attorney",
            FollowUpDate = "2026-08-10",
        });

        Assert.Equal("Requested", result.EventType);
        Assert.Equal("Open", result.Status);
        Assert.Equal("Review discovery responses", result.RequestedAction);
    }

    [Fact]
    public async Task RequestOrFollowUpAsync_RepeatedOnOpenThread_AppendsFollowUp_NotADuplicateThread()
    {
        var c = await CreateCaseAsync();
        await _fixture.Repository.RequestOrFollowUpAttorneyReminderAsync(c.Id, new RequestAttorneyReminderRequest
        {
            RequestedAction = "Review discovery responses",
            TargetAttorneyDisplay = "Sample Attorney",
            FollowUpDate = "2026-08-10",
        });
        var second = await _fixture.Repository.RequestOrFollowUpAttorneyReminderAsync(c.Id, new RequestAttorneyReminderRequest
        {
            FollowUpDate = "2026-08-17",
            Comment = "Still waiting.",
        });

        Assert.Equal("FollowUp", second.EventType);
        Assert.Equal("Review discovery responses", second.RequestedAction); // carried forward, not overwritten by an empty value
        Assert.Equal("2026-08-17", second.FollowUpDate);

        var history = await _fixture.Repository.GetReminderRequestsAsync(c.Id);
        Assert.Equal(2, history.Count); // append-only: two rows, one thread

        var open = await _fixture.Repository.GetOpenAttorneyRemindersAsync();
        var thread = Assert.Single(open, r => r.CaseId == c.Id);
        Assert.Equal("2026-08-17", thread.FollowUpDate); // reflects the latest row, not the first
    }

    [Fact]
    public async Task ResolveAsync_ClosesThread_AndSubsequentRequestStartsFresh()
    {
        var c = await CreateCaseAsync();
        await _fixture.Repository.RequestOrFollowUpAttorneyReminderAsync(c.Id, new RequestAttorneyReminderRequest
        {
            RequestedAction = "Review discovery responses",
            FollowUpDate = "2026-08-10",
        });

        var resolved = await _fixture.Repository.ResolveReminderAsync(c.Id, new ResolveReminderRequest { Comment = "Handled in person." });
        Assert.Equal("Resolved", resolved.Status);

        Assert.Empty(await _fixture.Repository.GetOpenAttorneyRemindersAsync());

        var third = await _fixture.Repository.RequestOrFollowUpAttorneyReminderAsync(c.Id, new RequestAttorneyReminderRequest
        {
            RequestedAction = "New unrelated ask",
            FollowUpDate = "2026-09-01",
        });
        Assert.Equal("Requested", third.EventType); // fresh thread, not a FollowUp on the resolved one

        var history = await _fixture.Repository.GetReminderRequestsAsync(c.Id);
        Assert.Equal(3, history.Count);
    }

    [Fact]
    public async Task ResolveAsync_WithNoOpenThread_Throws()
    {
        var c = await CreateCaseAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _fixture.Repository.ResolveReminderAsync(c.Id, new ResolveReminderRequest()));
    }

    [Fact]
    public async Task RequestOrFollowUpAsync_FirstCallWithoutRequestedAction_Throws()
    {
        var c = await CreateCaseAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => _fixture.Repository.RequestOrFollowUpAttorneyReminderAsync(c.Id, new RequestAttorneyReminderRequest
        {
            FollowUpDate = "2026-08-10",
        }));
    }

    [Fact]
    public async Task RelatedEventId_KeepsThreadsIndependent_ForTheSameCase()
    {
        var c = await CreateCaseAsync();
        await _fixture.Repository.RequestOrFollowUpAttorneyReminderAsync(c.Id, new RequestAttorneyReminderRequest
        {
            RelatedEventId = 501, RequestedAction = "Prep for hearing", FollowUpDate = "2026-08-10",
        });
        await _fixture.Repository.RequestOrFollowUpAttorneyReminderAsync(c.Id, new RequestAttorneyReminderRequest
        {
            RequestedAction = "General case-level ask", FollowUpDate = "2026-08-11",
        });

        var open = await _fixture.Repository.GetOpenAttorneyRemindersAsync();
        Assert.Equal(2, open.Count(r => r.CaseId == c.Id)); // event-scoped and case-level threads don't collide
    }

    // GetOpenAttorneyRemindersAsync backs both the Attorney Action Queue integration and the new
    // GET /api/reminders/open endpoint (Legal Assistant Dashboard audit Phase 6's division-wide
    // "waiting-on-attorney requests" panel), so it needs to span every case, not just one.
    [Fact]
    public async Task GetOpenAttorneyRemindersAsync_SpansMultipleCases()
    {
        var caseA = await CreateCaseAsync();
        var caseB = await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Second Reminder Test Case", County = "Pulaski", Status = "Active",
            CaseStatus = "Active Litigation", Track = "Contested",
        });

        await _fixture.Repository.RequestOrFollowUpAttorneyReminderAsync(caseA.Id, new RequestAttorneyReminderRequest
        {
            RequestedAction = "Ask A", FollowUpDate = "2026-08-10",
        });
        await _fixture.Repository.RequestOrFollowUpAttorneyReminderAsync(caseB.Id, new RequestAttorneyReminderRequest
        {
            RequestedAction = "Ask B", FollowUpDate = "2026-08-11",
        });

        var open = await _fixture.Repository.GetOpenAttorneyRemindersAsync();
        Assert.Contains(open, r => r.CaseId == caseA.Id);
        Assert.Contains(open, r => r.CaseId == caseB.Id);
    }
}
