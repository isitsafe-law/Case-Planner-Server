using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;
using CasePlanner.Web.Server.Security;

namespace CasePlanner.Web.Server.Tests;

// Covers the "reduce decision fatigue" pass: every dashboard quick action now leaves an
// activity-log trace, and bulk-defer applies to a batch of cases. (The issue-tag-driven
// discovery *content* tests that used to live here were retired in build-plan step 7 along with
// the legacy discovery-template-item system - see BuiltinDocumentTemplatesTests.cs for the
// unified platform's equivalent coverage.)
public class QuickActionActivityAndDiscoveryTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task<CaseRecord> CreateCaseAsync() =>
        await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Fixture Case",
            County = "Pulaski",
            Status = "Active",
            Track = "Contested",
        });

    [Fact]
    public async Task ActivityAudit_PersistsAuthenticatedActorOnEntryAndEditHistory()
    {
        var userId=Guid.NewGuid();
        await using var fixture=await RepositoryTestFixture.CreateAsync(new TestActor(userId,"Attorney Example"));
        var c=await fixture.Repository.SaveCaseAsync(new CaseRecord{CaseName="Audit Fixture",County="Pulaski",Status="Active",Track="Contested"});
        var entry=await fixture.Repository.RecordActivityAsync(c.Id,"Other","Initial note","2026-07-15");
        Assert.Equal(userId.ToString("D"),entry.ActorUserId);
        Assert.Equal("Attorney Example",entry.ActorDisplay);

        await fixture.Repository.UpdateActivityEntryAsync(entry.Id,new UpdateActivityRequest
        {
            ActivityType="Other",OccurredAt="2026-07-16",Notes="Corrected note",Reason="Corrected date"
        });
        var saved=Assert.Single(await fixture.Repository.GetActivityLogAsync(c.Id),x=>x.Id==entry.Id);
        var history=Assert.Single(saved.History);
        Assert.Equal(userId.ToString("D"),history.EditedByUserId);
        Assert.Equal("Attorney Example",history.EditedByDisplay);
    }

    private sealed record TestActor(Guid Id,string Label):IApplicationActorContext
    {
        public Guid? UserId=>Id;
        public string AuditLabel=>Label;
    }

    [Fact]
    public async Task SetNextActionAsync_WritesActivityLogEntry()
    {
        var c = await CreateCaseAsync();
        await _fixture.Repository.SetNextActionAsync(c.Id, new SetNextActionRequest { NextAction = "Call appraiser", NextReviewDate = "2026-08-01" });

        var log = await _fixture.Repository.GetActivityLogAsync(c.Id);
        Assert.Contains(log, e => e.ActivityType == "NextActionSet" && e.Notes == "Call appraiser");
    }

    [Fact]
    public async Task SetWaitingAsync_WritesActivityLogEntry()
    {
        var c = await CreateCaseAsync();
        await _fixture.Repository.SetWaitingAsync(c.Id, new SetWaitingRequest { WaitingOn = "Owner's appraiser", WaitingFollowUpDate = "2026-08-01" });

        var log = await _fixture.Repository.GetActivityLogAsync(c.Id);
        Assert.Contains(log, e => e.ActivityType == "MarkedWaiting" && e.Notes!.Contains("Owner's appraiser"));
    }

    [Fact]
    public async Task DeferActionAsync_WritesActivityLogEntry()
    {
        var c = await CreateCaseAsync();
        await _fixture.Repository.DeferActionAsync(c.Id, new DeferActionRequest { Reason = "Waiting on title work", FutureReviewDate = "2026-09-01" });

        var log = await _fixture.Repository.GetActivityLogAsync(c.Id);
        Assert.Contains(log, e => e.ActivityType == "CaseDeferred" && e.Notes!.Contains("Waiting on title work"));
    }

    [Fact]
    public async Task SetHolderAsync_WritesActivityLogEntry()
    {
        var c = await CreateCaseAsync();
        await _fixture.Repository.SetHolderAsync(c.Id, new SetHolderRequest { CurrentHolder = "Attorney" });

        var log = await _fixture.Repository.GetActivityLogAsync(c.Id);
        Assert.Contains(log, e => e.ActivityType == "HolderAssigned" && e.Notes!.Contains("Attorney"));
    }

    [Fact]
    public async Task SetShortNoteAsync_WritesActivityLogEntry()
    {
        var c = await CreateCaseAsync();
        await _fixture.Repository.SetShortNoteAsync(c.Id, "Called landowner, no answer");

        var log = await _fixture.Repository.GetActivityLogAsync(c.Id);
        Assert.Contains(log, e => e.ActivityType == "ShortNoteAdded" && e.Notes == "Called landowner, no answer");
    }

    [Fact]
    public async Task QuickActionActivityTypes_AreNotMeaningful()
    {
        // These are administrative/workflow actions, not case-progress events - they should be
        // visible in the activity trail but must not reset the 60-day momentum clock.
        var c = await CreateCaseAsync();
        await _fixture.Repository.SetHolderAsync(c.Id, new SetHolderRequest { CurrentHolder = "Attorney" });

        var log = await _fixture.Repository.GetActivityLogAsync(c.Id);
        var entry = Assert.Single(log, e => e.ActivityType == "HolderAssigned");
        Assert.False(entry.IsMeaningful);
    }

    [Fact]
    public async Task BulkDeferActionAsync_DefersEveryCaseInTheBatch()
    {
        var a = await CreateCaseAsync();
        var b = await CreateCaseAsync();

        await _fixture.Repository.BulkDeferActionAsync([a.Id, b.Id], new DeferActionRequest { Reason = "Batch review", FutureReviewDate = "2026-10-01" });

        foreach (var caseId in new[] { a.Id, b.Id })
        {
            var updated = (await _fixture.Repository.GetCaseWorkspaceAsync(caseId))!.Case;
            Assert.Equal("2026-10-01", updated.DeferredUntil);
            Assert.Equal("Batch review", updated.DeferredReason);
            Assert.NotNull(updated.DeferredAt);
            Assert.Equal("Local development user", updated.DeferredBy);

            var log = await _fixture.Repository.GetActivityLogAsync(caseId);
            Assert.Contains(log, e => e.ActivityType == "CaseDeferred");
        }
    }

    [Fact]
    public async Task ClearDefermentAsync_ClearsDedicatedFieldsAndWritesActivity()
    {
        var c = await CreateCaseAsync();
        await _fixture.Repository.DeferActionAsync(c.Id, new DeferActionRequest { FutureReviewDate = "2026-10-01" });
        await _fixture.Repository.ClearDefermentAsync(c.Id, "Ready for review");

        var updated = (await _fixture.Repository.GetCaseWorkspaceAsync(c.Id))!.Case;
        Assert.Null(updated.DeferredUntil);
        Assert.Null(updated.DeferredReason);
        var log = await _fixture.Repository.GetActivityLogAsync(c.Id);
        Assert.Contains(log, e => e.ActivityType == "CaseDefermentCleared" && e.Notes!.Contains("Ready for review"));
    }

    [Fact]
    public async Task SavePublicationRecordAsync_ValidatesDateOrderAndName()
    {
        var c = await CreateCaseAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _fixture.Repository.SavePublicationRecordAsync(new PublicationRecord
        {
            CaseId = c.Id, FirstPublicationDate = "2026-08-02", SecondPublicationDate = "2026-08-01", PublicationName = "Daily Record"
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _fixture.Repository.SavePublicationRecordAsync(new PublicationRecord
        {
            CaseId = c.Id, FirstPublicationDate = "2026-08-01"
        }));
    }

    [Fact]
    public async Task SavePublicationRecordAsync_PersistsCanonicalRecordAndActivity()
    {
        var c = await CreateCaseAsync();
        await _fixture.Repository.SavePublicationRecordAsync(new PublicationRecord
        {
            CaseId = c.Id,
            FirstPublicationDate = "2026-08-01",
            SecondPublicationDate = "2026-08-08",
            PublicationName = "Daily Record",
            MarkedPerfected = true
        });

        var saved = await _fixture.Repository.GetPublicationRecordAsync(c.Id);
        Assert.NotNull(saved);
        Assert.Equal("2026-08-01", saved.FirstPublicationDate);
        Assert.Equal("2026-08-08", saved.SecondPublicationDate);
        Assert.Equal("Daily Record", saved.PublicationName);
        Assert.True(saved.MarkedPerfected);
        Assert.Equal("Local development user", saved.LastUpdatedBy);
        var log = await _fixture.Repository.GetActivityLogAsync(c.Id);
        Assert.Contains(log, e => e.ActivityType == "PublicationChanged");
    }

    [Fact]
    public async Task GeneratedTasksAndDeadlines_StoreStructuredProvenance()
    {
        var c = await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Provenance Case", County = "Pulaski", Status = "Active", CaseStatus = "Active Litigation", Track = "Contested",
            Stage = "Intake & Filing", FilingDate = "2026-07-01"
        });
        await _fixture.Repository.GenerateChecklistAsync(c.Id);
        await _fixture.Repository.GenerateDeadlinesAsync(c.Id);

        var tasks = await _fixture.Repository.GetChecklistItemsAsync(c.Id);
        Assert.All(tasks.Where(x => !x.IsManual), x =>
        {
            Assert.Equal("StageTemplate", x.SourceKind);
            Assert.False(string.IsNullOrWhiteSpace(x.SourceTemplateId));
            Assert.Equal("Active Litigation", x.SourceStage);
            Assert.NotNull(x.GeneratedAt);
        });
        var deadlines = await _fixture.Repository.GetDeadlinesAsync(c.Id);
        Assert.All(deadlines.Where(x => !x.IsManual), x =>
        {
            Assert.Equal("DeadlineTemplate", x.SourceKind);
            Assert.False(string.IsNullOrWhiteSpace(x.SourceTemplateId));
            // Matches the current DeadlineTemplateVersion (bumped for the ARDOT workflow rewrite -
            // see CasePlannerRepository.GenerateDeadlinesForCaseAsync, which stamps generated
            // deadlines with int.Parse(DeadlineTemplateVersion)).
            Assert.Equal(5, x.SourceTemplateVersion);
            Assert.NotNull(x.GeneratedAt);
        });
    }

    [Fact]
    public async Task WorkTemplateReview_FlagsCompletedDuplicates_AndAddsAdjustedSelection()
    {
        var c = await _fixture.Repository.SaveCaseAsync(new CaseRecord { CaseName="Picker Case",County="Pulaski",Status="Active",CaseStatus="Active Litigation",Track="Contested",Stage="Intake & Filing",FilingDate="2026-07-01" });
        var candidates = await _fixture.Repository.GetWorkTemplateCandidatesAsync(c.Id);
        var task = candidates.First(x => x.Kind == "Task");
        await _fixture.Repository.SaveChecklistItemAsync(new ChecklistItemRecord { CaseId=c.Id,Phase=task.Stage,Task=task.Title,Status="Done",IsManual=true,SourceType="Manual" });
        candidates = await _fixture.Repository.GetWorkTemplateCandidatesAsync(c.Id);
        Assert.True(candidates.Single(x=>x.Kind=="Task"&&x.TemplateId==task.TemplateId).IsDuplicate);
        Assert.Contains("done", candidates.Single(x=>x.Kind=="Task"&&x.TemplateId==task.TemplateId).DuplicateReason!, StringComparison.OrdinalIgnoreCase);

        var deadline = candidates.First(x=>x.Kind=="Deadline"&&!x.IsDuplicate);
        var added = await _fixture.Repository.AddWorkTemplateSelectionsAsync(c.Id, new AddWorkTemplatesRequest { Items=[new AddWorkTemplateSelection { Kind="Deadline",TemplateId=deadline.TemplateId,DueDate="2026-12-31" }] });
        Assert.Equal(1,added);
        Assert.Contains(await _fixture.Repository.GetDeadlinesAsync(c.Id), x=>x.SourceTemplateId==deadline.TemplateId&&x.DueDate=="2026-12-31");
    }

    [Fact]
    public async Task DiscoveryCompletion_IsAuditedWithoutChangingItems()
    {
        var c = await CreateCaseAsync();
        var item = await _fixture.Repository.SaveDiscoveryItemAsync(new DiscoveryItemRecord { CaseId=c.Id, DiscoveryType="Interrogatories", Status="Waiting for Responses" });
        await _fixture.Repository.SaveDiscoveryPostureAsync(new DiscoveryPosture { CaseId=c.Id, IsComplete=true });
        var posture = await _fixture.Repository.GetDiscoveryPostureAsync(c.Id);
        Assert.True(posture!.IsComplete);
        Assert.NotNull(posture.CompletionChangedAt);
        Assert.Equal("Local development user", posture.CompletionChangedBy);
        Assert.Equal("Waiting for Responses", (await _fixture.Repository.GetDiscoveryItemsAsync(c.Id)).Single(x=>x.Id==item.Id).Status);
        Assert.Contains(await _fixture.Repository.GetActivityLogAsync(c.Id), x=>x.ActivityType=="DiscoveryCompleted");
        await _fixture.Repository.SaveDiscoveryPostureAsync(new DiscoveryPosture { CaseId=c.Id, IsComplete=false });
        Assert.Contains(await _fixture.Repository.GetActivityLogAsync(c.Id), x=>x.ActivityType=="DiscoveryReopened");
    }

    [Fact]
    public async Task NewManualCaseDefaultsToPipelineAndConsolidatedStatus()
    {
        var c = await _fixture.Repository.SaveCaseAsync(new CaseRecord { CaseName="Pipeline default", County="Pulaski" });
        var saved = (await _fixture.Repository.GetCaseWorkspaceAsync(c.Id))!.Case;
        Assert.Equal("Pipeline", saved.Status);
        Assert.Equal("Pipeline", saved.CaseStatus);
    }

    [Fact]
    public async Task ConsolidatedStatusMapsSettlementTrackAndTrialStage()
    {
        var settlement = await _fixture.Repository.SaveCaseAsync(new CaseRecord { CaseName="Settlement mapping", County="Pulaski", Status="Active", Track="Settlement", Stage="Discovery & Evaluation" });
        var trial = await _fixture.Repository.SaveCaseAsync(new CaseRecord { CaseName="Trial mapping", County="Pulaski", Status="Active", Track="Contested", Stage="Trial Track" });
        Assert.Equal("Settlement Pending", (await _fixture.Repository.GetCaseWorkspaceAsync(settlement.Id))!.Case.CaseStatus);
        Assert.Equal("Trial Preparation", (await _fixture.Repository.GetCaseWorkspaceAsync(trial.Id))!.Case.CaseStatus);
    }
}
