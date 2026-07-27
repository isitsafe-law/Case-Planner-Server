using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Security;
using Microsoft.Data.Sqlite;

namespace CasePlanner.Web.Server.Tests;

// Manager/Administrator Dashboard Milestone 4 correction coverage: the pre-filing milestone
// tracker (case_prefiling_milestones / PreFilingMilestoneGate) that replaces part of Milestone 2's
// Filing Approval gate, plus PipelinePromotionGate.EnsureFilingReady - the corrected check basis
// for a case leaving CaseStatus="Pipeline" - and its manager-override path. Mirrors
// SettlementAuthorityRequestTests's structure - a fresh RepositoryTestFixture per test, plain
// assertions against the real SQLite repository (no mocking). Exercises CasePlannerRepository's
// methods directly (the same surface SqlitePreFilingMilestoneStore just delegates to).
public class PreFilingMilestoneTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;

    // "Attorney" matches the "assigned attorney" persona that actually marks these milestones day
    // to day - the two manager-override tests below spin up their own separate fixtures with a
    // different actor role instead of relying on this one.
    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync(new RoleTestActor(Guid.NewGuid(), "Attorney"));
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private static readonly string[] Order =
    [
        "PleadingsPackageSent",
        "ChiefCounselSignaturesReceived",
        "DeclarationOfTakingSentToDirector",
        "DirectorSignatureReceived",
    ];

    private async Task<CaseRecord> CreatePipelineCaseAsync(string currentHolder = "Legal Assistant") =>
        await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Pre-Filing Milestone Fixture Case",
            County = "Pulaski",
            Status = "Pipeline",
            Track = "Contested",
            CurrentHolder = currentHolder,
        });

    private async Task<string[]> MarkAllFourAsync(long caseId)
    {
        var dates = new[] { "2026-01-10", "2026-01-15", "2026-01-20", "2026-01-25" };
        for (var i = 0; i < Order.Length; i++)
        {
            await _fixture.Repository.MarkPreFilingMilestoneAsync(caseId, Order[i], new MarkPreFilingMilestoneRequest { OccurredDate = dates[i] });
        }
        return dates;
    }

    // --- Sequential-order enforcement: marking ---

    [Fact]
    public async Task MarkAsync_PleadingsPackageSent_OnFreshCase_Succeeds()
    {
        var c = await CreatePipelineCaseAsync();

        var marked = await _fixture.Repository.MarkPreFilingMilestoneAsync(c.Id, "PleadingsPackageSent", new MarkPreFilingMilestoneRequest
        {
            OccurredDate = "2026-01-15",
            Note = "Complaint in Condemnation, Declaration of Taking, exhibit A.",
        });

        Assert.True(marked.IsMarked);
        Assert.Equal("2026-01-15", marked.OccurredDate);
        Assert.Equal("Complaint in Condemnation, Declaration of Taking, exhibit A.", marked.Note);
        Assert.NotNull(marked.MarkedAt);
        Assert.Equal("Attorney", marked.MarkedByRole);
    }

    [Fact]
    public async Task MarkAsync_ChiefCounselSignaturesReceived_BeforePleadingsPackageSent_ThrowsInvalidOperationException()
    {
        var c = await CreatePipelineCaseAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fixture.Repository.MarkPreFilingMilestoneAsync(c.Id, "ChiefCounselSignaturesReceived", new MarkPreFilingMilestoneRequest { OccurredDate = "2026-01-20" }));
        Assert.Contains("Pleadings Package Sent", ex.Message);
        Assert.Contains("Chief Counsel Signatures Received", ex.Message);

        var records = await _fixture.Repository.GetPreFilingMilestonesAsync(c.Id);
        Assert.Empty(records);
    }

    [Fact]
    public async Task MarkAsync_AllFourInCorrectOrder_SucceedsAndWritesActivityLogEntries()
    {
        var c = await CreatePipelineCaseAsync();
        var dates = await MarkAllFourAsync(c.Id);

        var records = await _fixture.Repository.GetPreFilingMilestonesAsync(c.Id);
        Assert.Equal(4, records.Count);
        Assert.All(records, r => Assert.True(r.IsMarked));

        var log = await _fixture.Repository.GetActivityLogAsync(c.Id);
        var markedEntries = log.Where(e => e.ActivityType == "PreFilingMilestoneMarked").OrderBy(e => e.Id).ToList();
        Assert.Equal(4, markedEntries.Count);
        for (var i = 0; i < Order.Length; i++)
        {
            Assert.Equal(Order[i], markedEntries[i].FieldChanged);
            Assert.Equal(dates[i], markedEntries[i].NewValue);
            Assert.Equal("Unmarked", markedEntries[i].PreviousValue);
            Assert.Equal("Attorney", markedEntries[i].ActorRoleAtAction);
            Assert.True(markedEntries[i].IsMeaningful);
        }
    }

    [Fact]
    public async Task MarkAsync_AlreadyMarkedMilestone_ThrowsInvalidOperationException()
    {
        var c = await CreatePipelineCaseAsync();
        await _fixture.Repository.MarkPreFilingMilestoneAsync(c.Id, "PleadingsPackageSent", new MarkPreFilingMilestoneRequest { OccurredDate = "2026-01-10" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fixture.Repository.MarkPreFilingMilestoneAsync(c.Id, "PleadingsPackageSent", new MarkPreFilingMilestoneRequest { OccurredDate = "2026-01-11" }));
        Assert.Contains("already marked", ex.Message);
    }

    // --- Sequential-order enforcement: un-marking ---

    [Fact]
    public async Task UnmarkAsync_EarlierMilestoneWhileLaterStillMarked_Throws_ThenSucceedsInCorrectOrder()
    {
        var c = await CreatePipelineCaseAsync();
        await MarkAllFourAsync(c.Id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fixture.Repository.UnmarkPreFilingMilestoneAsync(c.Id, "DeclarationOfTakingSentToDirector", new UnmarkPreFilingMilestoneRequest { Reason = "Testing order enforcement." }));
        Assert.Contains("Director Signature Received", ex.Message);

        // Un-marking DirectorSignatureReceived first, then DeclarationOfTakingSentToDirector,
        // succeeds in that order.
        var unmarkedLast = await _fixture.Repository.UnmarkPreFilingMilestoneAsync(c.Id, "DirectorSignatureReceived", new UnmarkPreFilingMilestoneRequest { Reason = "Signature needs to be redone." });
        Assert.False(unmarkedLast.IsMarked);

        var unmarkedThird = await _fixture.Repository.UnmarkPreFilingMilestoneAsync(c.Id, "DeclarationOfTakingSentToDirector", new UnmarkPreFilingMilestoneRequest { Reason = "Resending the Declaration of Taking." });
        Assert.False(unmarkedThird.IsMarked);
    }

    [Fact]
    public async Task UnmarkAsync_BlankReason_ThrowsArgumentException()
    {
        var c = await CreatePipelineCaseAsync();
        await _fixture.Repository.MarkPreFilingMilestoneAsync(c.Id, "PleadingsPackageSent", new MarkPreFilingMilestoneRequest { OccurredDate = "2026-01-10" });

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _fixture.Repository.UnmarkPreFilingMilestoneAsync(c.Id, "PleadingsPackageSent", new UnmarkPreFilingMilestoneRequest { Reason = "   " }));

        var reloaded = await _fixture.Repository.GetPreFilingMilestonesAsync(c.Id);
        Assert.True(Assert.Single(reloaded).IsMarked);
    }

    [Fact]
    public async Task UnmarkAsync_WithReason_SucceedsAndWritesActivityLogEntryContainingReason()
    {
        var c = await CreatePipelineCaseAsync();
        await _fixture.Repository.MarkPreFilingMilestoneAsync(c.Id, "PleadingsPackageSent", new MarkPreFilingMilestoneRequest { OccurredDate = "2026-01-10" });

        var unmarked = await _fixture.Repository.UnmarkPreFilingMilestoneAsync(c.Id, "PleadingsPackageSent", new UnmarkPreFilingMilestoneRequest { Reason = "Package was recalled for a correction." });
        Assert.False(unmarked.IsMarked);
        Assert.Null(unmarked.OccurredDate);

        var log = await _fixture.Repository.GetActivityLogAsync(c.Id);
        var entry = Assert.Single(log, e => e.ActivityType == "PreFilingMilestoneUnmarked");
        Assert.Equal("Package was recalled for a correction.", entry.Notes);
        Assert.Equal("PleadingsPackageSent", entry.FieldChanged);
        Assert.Equal("2026-01-10", entry.PreviousValue);
        Assert.Equal("Unmarked", entry.NewValue);
        Assert.True(entry.IsMeaningful);
    }

    [Fact]
    public async Task UnmarkAsync_AlreadyUnmarkedMilestone_ThrowsInvalidOperationException()
    {
        var c = await CreatePipelineCaseAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fixture.Repository.UnmarkPreFilingMilestoneAsync(c.Id, "PleadingsPackageSent", new UnmarkPreFilingMilestoneRequest { Reason = "Never marked in the first place." }));
        Assert.Contains("not currently marked", ex.Message);
    }

    // --- PipelinePromotionGate.EnsureFilingReady: the corrected Pipeline-exit gate ---

    [Fact]
    public async Task SaveCaseAsync_LeavingPipeline_BlockedWithoutDirectorSignatureMilestoneMarked()
    {
        var c = await CreatePipelineCaseAsync();
        // Zero pipeline_holder_approvals rows AND zero case_prefiling_milestones rows on file -
        // proves the NEW check (case_prefiling_milestones) is what's blocking this, not stale
        // Milestone 2 logic.
        var approvals = await _fixture.Repository.GetPipelineHolderApprovalsAsync(c.Id);
        Assert.Empty(approvals);
        var milestonesBefore = await _fixture.Repository.GetPreFilingMilestonesAsync(c.Id);
        Assert.Empty(milestonesBefore);

        var loaded = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == c.Id);
        loaded.CaseStatus = "Filed / Service Pending";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _fixture.Repository.SaveCaseAsync(loaded));
        Assert.Contains("Director signature", ex.Message);

        var reloaded = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == c.Id);
        Assert.Equal("Pipeline", reloaded.CaseStatus);
    }

    [Fact]
    public async Task SaveCaseAsync_LeavingPipeline_SucceedsOnceDirectorSignatureReceivedIsMarked()
    {
        var c = await CreatePipelineCaseAsync();
        await MarkAllFourAsync(c.Id);

        var loaded = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == c.Id);
        loaded.CaseStatus = "Filed / Service Pending";

        await _fixture.Repository.SaveCaseAsync(loaded);

        var reloaded = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == c.Id);
        Assert.Equal("Filed / Service Pending", reloaded.CaseStatus);
    }

    [Fact]
    public async Task SaveCaseAsync_LeavingPipeline_ManagerOverride_SucceedsWithoutDirectorSignature_AndWritesFilingGateOverriddenActivity()
    {
        await using var managerFixture = await RepositoryTestFixture.CreateAsync(new RoleTestActor(Guid.NewGuid(), "Manager"));
        var c = await managerFixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Manager Override Fixture Case", County = "Pulaski", Status = "Pipeline", Track = "Contested", CurrentHolder = "Legal Assistant",
        });

        var loaded = (await managerFixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == c.Id);
        loaded.CaseStatus = "Filed / Service Pending";
        loaded.FilingGateOverrideReason = "Director verbally confirmed signature; paperwork still in transit.";

        await managerFixture.Repository.SaveCaseAsync(loaded);

        var reloaded = (await managerFixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == c.Id);
        Assert.Equal("Filed / Service Pending", reloaded.CaseStatus);

        var log = await managerFixture.Repository.GetActivityLogAsync(c.Id);
        var entry = Assert.Single(log, e => e.ActivityType == "FilingGateOverridden");
        Assert.Equal("CaseStatus", entry.FieldChanged);
        Assert.Equal("Pipeline", entry.PreviousValue);
        Assert.Equal("Filed / Service Pending", entry.NewValue);
        Assert.Equal("Director verbally confirmed signature; paperwork still in transit.", entry.Notes);
        Assert.Equal("Manager", entry.ActorRoleAtAction);
        Assert.True(entry.IsMeaningful);
    }

    [Fact]
    public async Task SaveCaseAsync_LeavingPipeline_AttorneyOverrideAttempt_ThrowsInvalidOperationException_EvenWithReason()
    {
        await using var attorneyFixture = await RepositoryTestFixture.CreateAsync(new RoleTestActor(Guid.NewGuid(), "Attorney"));
        var c = await attorneyFixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Attorney Override Attempt Fixture Case", County = "Pulaski", Status = "Pipeline", Track = "Contested", CurrentHolder = "Legal Assistant",
        });

        var loaded = (await attorneyFixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == c.Id);
        loaded.CaseStatus = "Filed / Service Pending";
        loaded.FilingGateOverrideReason = "I really need this filed today.";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => attorneyFixture.Repository.SaveCaseAsync(loaded));
        Assert.Contains("Only a manager", ex.Message);

        var reloaded = (await attorneyFixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == c.Id);
        Assert.Equal("Pipeline", reloaded.CaseStatus);
    }

    // --- GetPreFilingMilestoneAgingAsync: data layer for the future "Needs Attention" tab ---

    [Fact]
    public async Task GetPreFilingMilestoneAgingAsync_BucketsCaseByFurthestMarkedMilestone_AndReportsUnmarkedCaseAsNone()
    {
        var withProgress = await CreatePipelineCaseAsync();
        var withNothing = await CreatePipelineCaseAsync();

        await _fixture.Repository.MarkPreFilingMilestoneAsync(withProgress.Id, "PleadingsPackageSent", new MarkPreFilingMilestoneRequest
        {
            OccurredDate = DateTime.UtcNow.AddDays(-10).ToString("yyyy-MM-dd"),
        });

        // The public API always stamps marked_at="now" - this test needs to prove the aging math
        // actually measures elapsed time since marked_at, so it backdates that one column directly
        // against the same throwaway SQLite file RepositoryTestFixture.DatabasePath exists to
        // support poking at directly.
        await using (var connection = new SqliteConnection($"Data Source={_fixture.DatabasePath}"))
        {
            await connection.OpenAsync();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE case_prefiling_milestones SET marked_at=@markedAt WHERE case_id=@caseId AND milestone='PleadingsPackageSent'";
            cmd.Parameters.AddWithValue("@markedAt", DateTime.UtcNow.AddDays(-10).ToString("O"));
            cmd.Parameters.AddWithValue("@caseId", withProgress.Id);
            await cmd.ExecuteNonQueryAsync();
        }

        var summary = await _fixture.Repository.GetPreFilingMilestoneAgingAsync();

        // Bucket counts are checked for internal consistency (each bucket's Count matches how many
        // per-case rows actually landed in it) rather than an exact absolute number, since the
        // fixture's fixed sample seed data (RepositoryTestFixture's doc comment: "2 seeded demo
        // cases") also contributes rows to these buckets.
        var progressBucket = summary.Buckets.Single(b => b.Milestone == "PleadingsPackageSent");
        Assert.Equal(summary.Cases.Count(cs => cs.FurthestMilestone == "PleadingsPackageSent"), progressBucket.Count);
        Assert.True(progressBucket.Count >= 1);
        var noneBucket = summary.Buckets.Single(b => b.Milestone == "None");
        Assert.Equal(summary.Cases.Count(cs => cs.FurthestMilestone == "None"), noneBucket.Count);
        Assert.True(noneBucket.Count >= 1);

        var progressCase = summary.Cases.Single(cs => cs.CaseId == withProgress.Id);
        Assert.Equal("PleadingsPackageSent", progressCase.FurthestMilestone);
        Assert.InRange(progressCase.DaysSinceMarked!.Value, 9, 11);

        var nothingCase = summary.Cases.Single(cs => cs.CaseId == withNothing.Id);
        Assert.Equal("None", nothingCase.FurthestMilestone);
        Assert.Null(nothingCase.DaysSinceMarked);
    }

    private sealed record RoleTestActor(Guid Id, string RoleLabel) : IApplicationActorContext
    {
        public Guid? UserId => Id;
        public string AuditLabel => RoleLabel;
        public string Role => RoleLabel;
    }
}
