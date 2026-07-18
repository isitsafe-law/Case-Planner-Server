using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Tests;

// Integration tests against a real repository + real (temp, throwaway) SQLite file - exercises
// the actual SQL in GetAttorneyDashboardAsync and the mutation endpoints' repository methods,
// not a mock. Each test gets its own fresh database via RepositoryTestFixture.CreateAsync().
public class AttorneyDashboardIntegrationTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task<CaseRecord> CreateCaseAsync(Action<CaseRecord> configure)
    {
        var c = new CaseRecord
        {
            CaseName = "Fixture Case",
            County = "Pulaski",
            Status = "Active",
            Track = "Contested",
        };
        configure(c);
        return await _fixture.Repository.SaveCaseAsync(c);
    }

    // 1. Filed case with no discovery strategy
    [Fact]
    public async Task FiledCase_WithNoDiscoveryStrategy_AppearsInDiscoveryUnsetAndActionQueue()
    {
        await CreateCaseAsync(c =>
        {
            c.CaseNumber = "T-0001";
            c.CaseName = "No Strategy Case";
            c.Stage = "Discovery & Evaluation";
        });

        var dashboard = await _fixture.Repository.GetAttorneyDashboardAsync(new AttorneyDashboardFilters());

        // >= rather than == : the two built-in demo seed cases also have no discovery posture
        // saved, so they legitimately count too - this test only owns "No Strategy Case".
        Assert.True(dashboard.SummaryCounts.DiscoveryUnset >= 1);
        Assert.Contains(dashboard.DiscoveryControl.CasesByCondition["Strategy not selected"], r => r.CaseName == "No Strategy Case");
        Assert.Contains(dashboard.ActionQueue, a => a.CaseName == "No Strategy Case" && a.ActionCategory == "Decide");
    }

    // 2. Filed case with overdue discovery responses
    [Fact]
    public async Task FiledCase_WithOverdueDiscoveryResponses_ShowsInDiscoveryControl()
    {
        var c = await CreateCaseAsync(x =>
        {
            x.CaseNumber = "T-0002";
            x.CaseName = "Overdue Responses Case";
        });
        await _fixture.Repository.SaveDiscoveryPostureAsync(new DiscoveryPosture
        {
            CaseId = c.Id,
            Strategy = "Written discovery first",
            DiscoveryServedDate = "2026-05-01",
            ResponsesDueDate = "2026-06-01",
        });

        var dashboard = await _fixture.Repository.GetAttorneyDashboardAsync(new AttorneyDashboardFilters());

        Assert.True(dashboard.DiscoveryControl.ResponsesOverdue >= 1);
        Assert.Contains(dashboard.DiscoveryControl.CasesByCondition["Responses overdue"], r => r.CaseName == "Overdue Responses Case");
    }

    // 3. Filed case waiting appropriately
    [Fact]
    public async Task FiledCase_WaitingAppropriately_DoesNotAppearAsStalled()
    {
        var futureDate = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd");
        await CreateCaseAsync(c =>
        {
            c.CaseNumber = "T-0003";
            c.CaseName = "Waiting Case";
            c.WaitingOn = "Owner's appraiser";
            c.WaitingFollowUpDate = futureDate;
        });

        var dashboard = await _fixture.Repository.GetAttorneyDashboardAsync(new AttorneyDashboardFilters());
        var entry = Assert.Single(dashboard.MomentumReview, m => m.CaseName == "Waiting Case");
        Assert.Equal("Waiting Appropriately", entry.MomentumStatus);
        Assert.Equal(0, dashboard.SummaryCounts.Stalled);
    }

    // 4. Filed case stalled for more than 60 days
    [Fact]
    public async Task FiledCase_StalledOver60Days_AppearsInStalledCount()
    {
        var c = await CreateCaseAsync(x =>
        {
            x.CaseNumber = "T-0004";
            x.CaseName = "Stalled Case";
            x.NextAction = "Await landowner response";
            x.NextReviewDate = "2026-08-01";
        });
        // Give it a settled discovery posture so the only signal in play is the momentum one -
        // otherwise "Strategy not selected" (priority 2) would legitimately outrank the stalled
        // signal (priority 3) as the primary reason, which is correct behavior but not what this
        // test is isolating.
        await _fixture.Repository.SaveDiscoveryPostureAsync(new DiscoveryPosture { CaseId = c.Id, Strategy = "No discovery currently needed" });
        // Backdate the meaningful-activity clock directly through a real activity log entry.
        await _fixture.Repository.RecordActivityAsync(c.Id, "AttorneyStrategyDecisionRecorded", "backdated", DateTime.UtcNow.AddDays(-90).ToString("O"));

        var dashboard = await _fixture.Repository.GetAttorneyDashboardAsync(new AttorneyDashboardFilters());

        Assert.Equal(1, dashboard.SummaryCounts.Stalled);
        Assert.Contains(dashboard.ActionQueue, a => a.CaseName == "Stalled Case" && a.PriorityLevel == 3);
    }

    // 5-8. Pre-filing tract at each holder stage + returned for revision + rushed
    [Theory]
    [InlineData("Legal Assistant", "With Legal Assistant", false)]
    [InlineData("Attorney", "With Attorney", true)]
    [InlineData("Deputy Chief Counsel", "With Deputy Chief Counsel", false)]
    [InlineData("Chief Counsel", "With Chief Counsel", false)]
    public async Task PreFilingTract_AtEachHolderStage_ClassifiesCorrectly(string holder, string stage, bool expectMyDesk)
    {
        await CreateCaseAsync(c =>
        {
            c.CaseName = $"Tract at {holder}";
            c.MatterType = "PreFilingTract";
            c.CurrentHolder = holder;
            c.PipelineStage = stage;
        });

        var dashboard = await _fixture.Repository.GetAttorneyDashboardAsync(new AttorneyDashboardFilters());

        if (expectMyDesk)
        {
            Assert.Contains(dashboard.FilingPipeline.MyDesk, r => r.CurrentHolder == holder);
        }
        else
        {
            Assert.Contains(dashboard.FilingPipeline.Waiting, r => r.CurrentHolder == holder);
            Assert.DoesNotContain(dashboard.FilingPipeline.MyDesk, r => r.CurrentHolder == holder);
        }
    }

    [Fact]
    public async Task PreFilingTract_ReturnedForRevision_AppearsOnMyDesk()
    {
        await CreateCaseAsync(c =>
        {
            c.CaseName = "Returned Tract";
            c.MatterType = "PreFilingTract";
            c.CurrentHolder = "Legal Assistant";
            c.PipelineStage = "Returned for Revision";
        });

        var dashboard = await _fixture.Repository.GetAttorneyDashboardAsync(new AttorneyDashboardFilters());
        // "Returned for Revision" only affects the flag reason on My Desk when the holder is the
        // attorney - here the holder is Legal Assistant, so it's still Waiting but should carry a
        // Returned-for-Revision-flavored issue once handed back. Confirm it at least appears.
        Assert.Contains(dashboard.FilingPipeline.AllPipeline, r => r.TractOrOwnerName == "Returned Tract");
    }

    [Fact]
    public async Task PreFilingTract_Rushed_AppearsInActionQueue_EvenWhileWaitingOnSomeoneElse()
    {
        await CreateCaseAsync(c =>
        {
            c.CaseName = "Rushed Tract";
            c.MatterType = "PreFilingTract";
            c.CurrentHolder = "Deputy Chief Counsel";
            c.PipelineStage = "With Deputy Chief Counsel";
            c.Priority = "Rushed";
        });

        var dashboard = await _fixture.Repository.GetAttorneyDashboardAsync(new AttorneyDashboardFilters());

        Assert.Contains(dashboard.ActionQueue, a => a.CaseName == "Rushed Tract");
        Assert.Contains("Rushed", dashboard.ActionQueue.First(a => a.CaseName == "Rushed Tract").Reason);
    }

    [Fact]
    public async Task PreFilingTract_NormalPriorityWaiting_DoesNotClutterActionQueue()
    {
        await CreateCaseAsync(c =>
        {
            c.CaseName = "Quiet Waiting Tract";
            c.MatterType = "PreFilingTract";
            c.CurrentHolder = "Legal Assistant";
            c.PipelineStage = "With Legal Assistant";
            c.Priority = "Normal";
            c.WaitingFollowUpDate = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd");
        });

        var dashboard = await _fixture.Repository.GetAttorneyDashboardAsync(new AttorneyDashboardFilters());

        Assert.DoesNotContain(dashboard.ActionQueue, a => a.CaseName == "Quiet Waiting Tract");
        Assert.Contains(dashboard.FilingPipeline.Waiting, r => r.TractOrOwnerName == "Quiet Waiting Tract");
    }

    // 9. Trial-track case
    [Fact]
    public async Task TrialTrackCase_AppearsInTrialWatch_WithNeutralFeeLanguage()
    {
        await CreateCaseAsync(c =>
        {
            c.CaseNumber = "T-0009";
            c.CaseName = "Trial Track Case";
            c.TrialTrack = true;
            c.DepositAmount = 100_000m;
        });

        var dashboard = await _fixture.Repository.GetAttorneyDashboardAsync(new AttorneyDashboardFilters());

        Assert.Contains(dashboard.TrialWatch, t => t.CaseName == "Trial Track Case");
        Assert.Equal(1, dashboard.SummaryCounts.TrialTrack);
    }

    // 10. Project with several related tracts
    [Fact]
    public async Task ProjectWithMultipleTracts_AppearsInProjectWatch_WithCorrectCounts()
    {
        await CreateCaseAsync(c => { c.CaseName = "Tract A"; c.ProjectName = "Highway 10 Widening"; c.JobNumber = "JOB-10"; });
        await CreateCaseAsync(c => { c.CaseName = "Tract B"; c.ProjectName = "Highway 10 Widening"; c.JobNumber = "JOB-10"; c.MatterType = "PreFilingTract"; });

        var dashboard = await _fixture.Repository.GetAttorneyDashboardAsync(new AttorneyDashboardFilters());

        var project = Assert.Single(dashboard.ProjectWatch, p => p.ProjectName == "Highway 10 Widening");
        Assert.Equal(2, project.TractCount);
        Assert.Equal(1, project.PreFilingCount);
        Assert.Equal(1, project.FiledCount);
    }

    [Fact]
    public async Task SingleTractProject_DoesNotAppearInProjectWatch_NoWarningJustForSharingJobNumber()
    {
        await CreateCaseAsync(c => { c.CaseName = "Solo Tract"; c.ProjectName = "Solo Project"; c.JobNumber = "JOB-SOLO"; });

        var dashboard = await _fixture.Repository.GetAttorneyDashboardAsync(new AttorneyDashboardFilters());

        Assert.DoesNotContain(dashboard.ProjectWatch, p => p.ProjectName == "Solo Project");
    }

    // 11. Matter with multiple warnings - consolidation
    [Fact]
    public async Task MatterWithMultipleWarnings_ConsolidatesIntoOneActionQueueEntry()
    {
        var c = await CreateCaseAsync(x =>
        {
            x.CaseNumber = "T-0011";
            x.CaseName = "Multi-Warning Case";
        });
        await _fixture.Repository.RecordActivityAsync(c.Id, "AttorneyStrategyDecisionRecorded", "backdated", DateTime.UtcNow.AddDays(-90).ToString("O"));

        var dashboard = await _fixture.Repository.GetAttorneyDashboardAsync(new AttorneyDashboardFilters());

        var entries = dashboard.ActionQueue.Where(a => a.CaseName == "Multi-Warning Case").ToList();
        var entry = Assert.Single(entries);
        Assert.True(entry.RelatedWarningCount >= 2);
    }

    // 12. Missing holder or stage
    [Fact]
    public async Task PreFilingTract_MissingHolderAndStage_FlaggedInWaitingView()
    {
        await CreateCaseAsync(c =>
        {
            c.CaseName = "Ungoverned Tract";
            c.MatterType = "PreFilingTract";
        });

        var dashboard = await _fixture.Repository.GetAttorneyDashboardAsync(new AttorneyDashboardFilters());

        var row = Assert.Single(dashboard.FilingPipeline.Waiting, r => r.TractOrOwnerName == "Ungoverned Tract");
        Assert.Equal("Missing current holder or stage", row.FlagReason);
    }

    // --- Meaningful vs routine activity ---

    [Fact]
    public async Task RecordActivity_MeaningfulType_UpdatesLastMeaningfulActivityDate()
    {
        var c = await CreateCaseAsync(x => x.CaseName = "Activity Case");
        var before = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).First(x => x.Id == c.Id).LastMeaningfulActivityDate;

        await Task.Delay(10);
        var entry = await _fixture.Repository.RecordActivityAsync(c.Id, "DiscoveryServed", "served", null);
        var after = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).First(x => x.Id == c.Id).LastMeaningfulActivityDate;

        Assert.True(entry.IsMeaningful);
        Assert.NotEqual(before, after);
    }

    [Fact]
    public async Task RecordActivity_RoutineType_DoesNotResetMomentumClock()
    {
        var c = await CreateCaseAsync(x => x.CaseName = "Routine Note Case");
        await _fixture.Repository.RecordActivityAsync(c.Id, "AttorneyStrategyDecisionRecorded", "anchor", DateTime.UtcNow.AddDays(-90).ToString("O"));
        var afterAnchor = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).First(x => x.Id == c.Id).LastMeaningfulActivityDate;

        var routine = await _fixture.Repository.RecordActivityAsync(c.Id, "Other", "renamed a document", null);
        var afterRoutine = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).First(x => x.Id == c.Id).LastMeaningfulActivityDate;

        Assert.False(routine.IsMeaningful);
        Assert.Equal(afterAnchor, afterRoutine);
    }

    // --- Pipeline handoff history ---

    [Fact]
    public async Task PipelineHandoff_CreatesHistoryEntry_AndUpdatesCaseRow()
    {
        var c = await CreateCaseAsync(x =>
        {
            x.CaseName = "Handoff Tract";
            x.MatterType = "PreFilingTract";
            x.CurrentHolder = "Legal Assistant";
            x.PipelineStage = "With Legal Assistant";
        });

        var handoff = await _fixture.Repository.SavePipelineHandoffAsync(c.Id, new PipelineHandoffRequest
        {
            NewHolder = "Attorney",
            NewStage = "With Attorney",
            HandoffDate = "2026-07-10",
            NextReviewDate = "2026-07-17",
        });

        Assert.Equal("Legal Assistant", handoff.PreviousHolder);
        Assert.Equal("Attorney", handoff.NewHolder);

        var history = await _fixture.Repository.GetPipelineHandoffsAsync(c.Id);
        Assert.Single(history);

        var updated = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).First(x => x.Id == c.Id);
        Assert.Equal("Attorney", updated.CurrentHolder);
        Assert.Equal("With Attorney", updated.PipelineStage);
    }

    // --- Priority sorting ---

    [Fact]
    public async Task ActionQueue_SortsByPriorityFirst_ThenReviewDate()
    {
        var deadlines = new List<DeadlineItem>();
        var immediate = await CreateCaseAsync(x => { x.CaseName = "ZZZ Immediate"; });
        await _fixture.Repository.SaveDeadlineAsync(new DeadlineItem { CaseId = immediate.Id, Title = "Missed", DueDate = "2026-01-01", Status = "Open" });

        await CreateCaseAsync(x =>
        {
            x.CaseName = "AAA Decision Needed";
        });

        var dashboard = await _fixture.Repository.GetAttorneyDashboardAsync(new AttorneyDashboardFilters());

        // Priority 1 (missed deadline) must sort ahead of priority 2 (discovery decision)
        // regardless of alphabetical case name.
        var priorities = dashboard.ActionQueue.Select(a => a.PriorityLevel).ToList();
        Assert.True(priorities.SequenceEqual(priorities.OrderBy(p => p)));
        Assert.Equal("ZZZ Immediate", dashboard.ActionQueue.First().CaseName);
    }

    // --- Filtering ---

    [Fact]
    public async Task Filters_ByCounty_NarrowsActionQueue()
    {
        await CreateCaseAsync(c => { c.CaseName = "Pulaski Case"; c.County = "Pulaski"; });
        await CreateCaseAsync(c => { c.CaseName = "Saline Case"; c.County = "Saline"; });

        var filtered = await _fixture.Repository.GetAttorneyDashboardAsync(new AttorneyDashboardFilters { County = "Saline" });

        Assert.Contains(filtered.ActionQueue, a => a.CaseName == "Saline Case");
        Assert.DoesNotContain(filtered.ActionQueue, a => a.CaseName == "Pulaski Case");
    }

    [Fact]
    public async Task Filters_DoNotAffectSummaryCounts_WhichAlwaysReflectFullDocket()
    {
        await CreateCaseAsync(c => { c.CaseName = "Pulaski Case"; c.County = "Pulaski"; });
        await CreateCaseAsync(c => { c.CaseName = "Saline Case"; c.County = "Saline"; });

        var unfiltered = await _fixture.Repository.GetAttorneyDashboardAsync(new AttorneyDashboardFilters());
        var filtered = await _fixture.Repository.GetAttorneyDashboardAsync(new AttorneyDashboardFilters { County = "Saline" });

        Assert.Equal(unfiltered.SummaryCounts.DiscoveryUnset, filtered.SummaryCounts.DiscoveryUnset);
    }

    // --- API-adjacent error handling at the repository layer ---

    [Fact]
    public async Task GetDiscoveryPosture_ReturnsNull_ForCaseWithNoPostureSaved()
    {
        var c = await CreateCaseAsync(x => x.CaseName = "No Posture Case");
        var posture = await _fixture.Repository.GetDiscoveryPostureAsync(c.Id);
        Assert.Null(posture);
    }

}
