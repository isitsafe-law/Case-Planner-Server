using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Security;

namespace CasePlanner.Web.Server.Tests;

// Manager/Administrator Dashboard Milestone 3 (Settlement Authority workflow) coverage: the
// request/decide lifecycle backing the real settlement_authority_requests table, and
// CaseRecord.SettlementAuthorizedCeiling as the granted-ceiling side effect of an Approved
// decision. Mirrors PipelineHolderApprovalTests's structure - a fresh RepositoryTestFixture per
// test, plain assertions against the real SQLite repository (no mocking). Exercises
// CasePlannerRepository's methods directly (the same surface SqliteSettlementAuthorityRequestStore
// just delegates to) rather than going through Program.cs's HTTP endpoints, matching how
// PipelineHolderApprovalTests/ManagerTierAndActorRoleTests test the repository layer.
public class SettlementAuthorityRequestTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync(new RoleTestActor(Guid.NewGuid(), "Chief Counsel"));
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task<CaseRecord> CreateCaseAsync(string name = "Settlement Authority Fixture Case") =>
        await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = name,
            County = "Pulaski",
            Status = "Active",
            Track = "Contested",
        });

    // --- CreateSettlementAuthorityRequestAsync: the initial ask ---

    [Fact]
    public async Task CreateAsync_OnCaseWithNoOpenThread_Succeeds()
    {
        var c = await CreateCaseAsync();

        var created = await _fixture.Repository.CreateSettlementAuthorityRequestAsync(c.Id, new CreateSettlementAuthorityRequest
        {
            RequestedAmount = 50_000m,
            RequestingAttorney = "Jane Attorney",
            RequestNotes = "Owner is willing to settle near appraised value.",
        });

        Assert.NotEqual(0, created.Id);
        Assert.Equal(c.Id, created.CaseId);
        Assert.Equal(50_000m, created.RequestedAmount);
        Assert.Equal("Jane Attorney", created.RequestingAttorney);
        Assert.Equal("Pending", created.Status);
        Assert.Null(created.GrantedAmount);
        Assert.NotEmpty(created.RequestedAt);

        var list = await _fixture.Repository.GetSettlementAuthorityRequestsAsync(c.Id);
        var reloaded = Assert.Single(list);
        Assert.Equal("Pending", reloaded.Status);
    }

    [Fact]
    public async Task CreateAsync_SecondRequestWhileOnePending_ThrowsInvalidOperationException()
    {
        var c = await CreateCaseAsync();
        await _fixture.Repository.CreateSettlementAuthorityRequestAsync(c.Id, new CreateSettlementAuthorityRequest { RequestedAmount = 40_000m });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fixture.Repository.CreateSettlementAuthorityRequestAsync(c.Id, new CreateSettlementAuthorityRequest { RequestedAmount = 45_000m }));
        Assert.Contains("open Settlement Authority request", ex.Message);

        var list = await _fixture.Repository.GetSettlementAuthorityRequestsAsync(c.Id);
        Assert.Single(list);
    }

    [Fact]
    public async Task CreateAsync_WhileMostRecentThreadIsInfoRequested_StillCountsAsOpen_ThrowsInvalidOperationException()
    {
        var c = await CreateCaseAsync();
        var first = await _fixture.Repository.CreateSettlementAuthorityRequestAsync(c.Id, new CreateSettlementAuthorityRequest { RequestedAmount = 40_000m });
        await _fixture.Repository.DecideSettlementAuthorityRequestAsync(first.Id, new DecideSettlementAuthorityRequest { Action = "InfoRequested", Comment = "Need updated appraisal." });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fixture.Repository.CreateSettlementAuthorityRequestAsync(c.Id, new CreateSettlementAuthorityRequest { RequestedAmount = 45_000m }));
    }

    [Fact]
    public async Task CreateAsync_AfterPriorRequestWasDecided_Succeeds()
    {
        var c = await CreateCaseAsync();
        var first = await _fixture.Repository.CreateSettlementAuthorityRequestAsync(c.Id, new CreateSettlementAuthorityRequest { RequestedAmount = 40_000m });
        await _fixture.Repository.DecideSettlementAuthorityRequestAsync(first.Id, new DecideSettlementAuthorityRequest { Action = "Denied", Comment = "Not supportable yet." });

        var second = await _fixture.Repository.CreateSettlementAuthorityRequestAsync(c.Id, new CreateSettlementAuthorityRequest { RequestedAmount = 42_000m });
        Assert.NotEqual(0, second.Id);
    }

    [Fact]
    public async Task CreateAsync_WritesSettlementAuthorityRequestedActivityLogEntry()
    {
        var c = await CreateCaseAsync();
        await _fixture.Repository.CreateSettlementAuthorityRequestAsync(c.Id, new CreateSettlementAuthorityRequest
        {
            RequestedAmount = 50_000m,
            RequestNotes = "Initial ask notes.",
        });

        var log = await _fixture.Repository.GetActivityLogAsync(c.Id);
        var entry = Assert.Single(log, e => e.ActivityType == "SettlementAuthorityRequested");
        Assert.Equal("Initial ask notes.", entry.Notes);
        Assert.True(entry.IsMeaningful);
    }

    // --- DecideSettlementAuthorityRequestAsync: Approved ---

    [Fact]
    public async Task DecideAsync_Approved_DefaultsGrantedAmountToRequestedAmount_AndSetsCaseCeiling()
    {
        var c = await CreateCaseAsync();
        var request = await _fixture.Repository.CreateSettlementAuthorityRequestAsync(c.Id, new CreateSettlementAuthorityRequest { RequestedAmount = 60_000m });

        var decided = await _fixture.Repository.DecideSettlementAuthorityRequestAsync(request.Id, new DecideSettlementAuthorityRequest
        {
            Action = "Approved",
            Comment = "Approved as requested.",
        });

        Assert.Equal("Approved", decided.Status);
        Assert.Equal(60_000m, decided.GrantedAmount);
        Assert.Equal("Chief Counsel", decided.DecidedByRole);
        Assert.Equal("Approved as requested.", decided.DecisionComment);
        Assert.NotNull(decided.DecidedAt);

        var reloadedCase = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == c.Id);
        Assert.Equal(60_000m, reloadedCase.SettlementAuthorizedCeiling);
    }

    [Fact]
    public async Task DecideAsync_Approved_WithExplicitGrantedAmount_OverridesRequestedAmount()
    {
        var c = await CreateCaseAsync();
        var request = await _fixture.Repository.CreateSettlementAuthorityRequestAsync(c.Id, new CreateSettlementAuthorityRequest { RequestedAmount = 60_000m });

        var decided = await _fixture.Repository.DecideSettlementAuthorityRequestAsync(request.Id, new DecideSettlementAuthorityRequest
        {
            Action = "Approved",
            Comment = "Approved for a lower amount.",
            GrantedAmount = 45_000m,
        });

        Assert.Equal(45_000m, decided.GrantedAmount);

        var reloadedCase = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == c.Id);
        Assert.Equal(45_000m, reloadedCase.SettlementAuthorizedCeiling);
    }

    [Fact]
    public async Task DecideAsync_Approved_WritesSettlementAuthorityReceivedActivityLogEntry_WithDiffFields()
    {
        var c = await CreateCaseAsync();
        var request = await _fixture.Repository.CreateSettlementAuthorityRequestAsync(c.Id, new CreateSettlementAuthorityRequest { RequestedAmount = 60_000m });

        await _fixture.Repository.DecideSettlementAuthorityRequestAsync(request.Id, new DecideSettlementAuthorityRequest
        {
            Action = "Approved",
            Comment = "Approved as requested.",
        });

        var log = await _fixture.Repository.GetActivityLogAsync(c.Id);
        var entry = Assert.Single(log, e => e.ActivityType == "SettlementAuthorityReceived");
        Assert.Equal("Chief Counsel", entry.ActorRoleAtAction);
        Assert.Equal("SettlementAuthorizedCeiling", entry.FieldChanged);
        Assert.Equal("none", entry.PreviousValue);
        Assert.Equal("60000.00", entry.NewValue);
        Assert.Equal("Approved as requested.", entry.Notes);
        Assert.True(entry.IsMeaningful);
    }

    [Fact]
    public async Task DecideAsync_Approved_PreviousValueReflectsPriorCeilingWhenOneAlreadyExisted()
    {
        var c = await CreateCaseAsync();
        var first = await _fixture.Repository.CreateSettlementAuthorityRequestAsync(c.Id, new CreateSettlementAuthorityRequest { RequestedAmount = 60_000m });
        await _fixture.Repository.DecideSettlementAuthorityRequestAsync(first.Id, new DecideSettlementAuthorityRequest { Action = "Approved", Comment = "First approval." });

        var second = await _fixture.Repository.CreateSettlementAuthorityRequestAsync(c.Id, new CreateSettlementAuthorityRequest { RequestedAmount = 75_000m });
        await _fixture.Repository.DecideSettlementAuthorityRequestAsync(second.Id, new DecideSettlementAuthorityRequest { Action = "Approved", Comment = "Second approval, higher ceiling." });

        var log = await _fixture.Repository.GetActivityLogAsync(c.Id);
        var entries = log.Where(e => e.ActivityType == "SettlementAuthorityReceived").OrderBy(e => e.Id).ToList();
        Assert.Equal(2, entries.Count);
        Assert.Equal("none", entries[0].PreviousValue);
        Assert.Equal("60000.00", entries[0].NewValue);
        Assert.Equal("60000.00", entries[1].PreviousValue);
        Assert.Equal("75000.00", entries[1].NewValue);

        var reloadedCase = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == c.Id);
        Assert.Equal(75_000m, reloadedCase.SettlementAuthorizedCeiling);
    }

    // --- Manager Dashboard sign-off consolidation, item 4: granter/date-granted/doc-reference ---

    [Fact]
    public async Task DecideAsync_Approved_RecordsGranterDetailsSeparateFromWhoRecordedIt()
    {
        var c = await CreateCaseAsync();
        var request = await _fixture.Repository.CreateSettlementAuthorityRequestAsync(c.Id, new CreateSettlementAuthorityRequest { RequestedAmount = 60_000m });

        var decided = await _fixture.Repository.DecideSettlementAuthorityRequestAsync(request.Id, new DecideSettlementAuthorityRequest
        {
            Action = "Approved",
            Comment = "Director confirmed verbally; recording after the fact.",
            GrantedBy = "Michelle Davenport",
            GrantedByRole = "Chief Counsel",
            GrantedDate = "2026-07-01",
            DocumentReference = "Email thread \"Tract 14 settlement authority\" dated 2026-06-30",
        });

        Assert.Equal("Michelle Davenport", decided.GrantedBy);
        Assert.Equal("Chief Counsel", decided.GrantedByRole);
        Assert.Equal("2026-07-01", decided.GrantedDate);
        Assert.Equal("Email thread \"Tract 14 settlement authority\" dated 2026-06-30", decided.DocumentReference);
        // DecidedByRole reflects who RECORDED the entry (the fixture's actor), independent of
        // GrantedByRole (who actually granted it) - these can legitimately differ.
        Assert.Equal("Chief Counsel", decided.DecidedByRole);
    }

    [Fact]
    public async Task DecideAsync_Approved_WithoutExplicitGrantedDate_DefaultsToToday()
    {
        var c = await CreateCaseAsync();
        var request = await _fixture.Repository.CreateSettlementAuthorityRequestAsync(c.Id, new CreateSettlementAuthorityRequest { RequestedAmount = 60_000m });

        var decided = await _fixture.Repository.DecideSettlementAuthorityRequestAsync(request.Id, new DecideSettlementAuthorityRequest
        {
            Action = "Approved",
            Comment = "Approved as requested.",
        });

        Assert.Equal(DateTime.UtcNow.ToString("yyyy-MM-dd"), decided.GrantedDate);
        Assert.Null(decided.GrantedBy);
        Assert.Null(decided.GrantedByRole);
    }

    [Fact]
    public async Task DecideAsync_Denied_NeverPopulatesGranterFields_ButStillAcceptsADocumentReference()
    {
        var c = await CreateCaseAsync();
        var request = await _fixture.Repository.CreateSettlementAuthorityRequestAsync(c.Id, new CreateSettlementAuthorityRequest { RequestedAmount = 60_000m });

        var decided = await _fixture.Repository.DecideSettlementAuthorityRequestAsync(request.Id, new DecideSettlementAuthorityRequest
        {
            Action = "Denied",
            Comment = "Not supportable given current valuation.",
            GrantedBy = "Should be ignored - no grant on a Denied outcome.",
            DocumentReference = "Valuation memo v2",
        });

        Assert.Null(decided.GrantedBy);
        Assert.Null(decided.GrantedByRole);
        Assert.Null(decided.GrantedDate);
        Assert.Equal("Valuation memo v2", decided.DocumentReference);
    }

    // --- DecideSettlementAuthorityRequestAsync: Denied / InfoRequested leave the ceiling alone ---

    [Fact]
    public async Task DecideAsync_Denied_LeavesCaseCeilingNull()
    {
        var c = await CreateCaseAsync();
        var request = await _fixture.Repository.CreateSettlementAuthorityRequestAsync(c.Id, new CreateSettlementAuthorityRequest { RequestedAmount = 60_000m });

        var decided = await _fixture.Repository.DecideSettlementAuthorityRequestAsync(request.Id, new DecideSettlementAuthorityRequest
        {
            Action = "Denied",
            Comment = "Not supportable given current valuation.",
        });

        Assert.Equal("Denied", decided.Status);
        Assert.Null(decided.GrantedAmount);

        var reloadedCase = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == c.Id);
        Assert.Null(reloadedCase.SettlementAuthorizedCeiling);

        var log = await _fixture.Repository.GetActivityLogAsync(c.Id);
        var entry = Assert.Single(log, e => e.ActivityType == "SettlementAuthorityDenied");
        Assert.Equal("SettlementAuthorizedCeiling", entry.FieldChanged);
        Assert.Equal("none", entry.PreviousValue);
        Assert.Equal("Denied", entry.NewValue);
        Assert.Equal("Chief Counsel", entry.ActorRoleAtAction);
    }

    [Fact]
    public async Task DecideAsync_InfoRequested_LeavesCaseCeilingUnchanged()
    {
        var c = await CreateCaseAsync();
        // Establish a pre-existing ceiling from an earlier approved request, so this test can prove
        // InfoRequested leaves a NON-null prior value untouched too, not just "stays null".
        var earlier = await _fixture.Repository.CreateSettlementAuthorityRequestAsync(c.Id, new CreateSettlementAuthorityRequest { RequestedAmount = 30_000m });
        await _fixture.Repository.DecideSettlementAuthorityRequestAsync(earlier.Id, new DecideSettlementAuthorityRequest { Action = "Approved", Comment = "Baseline approval." });

        var request = await _fixture.Repository.CreateSettlementAuthorityRequestAsync(c.Id, new CreateSettlementAuthorityRequest { RequestedAmount = 60_000m });
        var decided = await _fixture.Repository.DecideSettlementAuthorityRequestAsync(request.Id, new DecideSettlementAuthorityRequest
        {
            Action = "InfoRequested",
            Comment = "Need an updated comparable sales analysis first.",
        });

        Assert.Equal("InfoRequested", decided.Status);
        Assert.Null(decided.GrantedAmount);

        var reloadedCase = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == c.Id);
        Assert.Equal(30_000m, reloadedCase.SettlementAuthorizedCeiling);

        var log = await _fixture.Repository.GetActivityLogAsync(c.Id);
        var entry = Assert.Single(log, e => e.ActivityType == "SettlementAuthorityInfoRequested");
        Assert.Equal("SettlementAuthorizedCeiling", entry.FieldChanged);
        Assert.Equal("30000.00", entry.PreviousValue);
        Assert.Equal("InfoRequested", entry.NewValue);
    }

    // --- Validation ---

    [Theory]
    [InlineData("Approved")]
    [InlineData("Denied")]
    [InlineData("InfoRequested")]
    public async Task DecideAsync_BlankComment_ThrowsArgumentException_RegardlessOfAction(string action)
    {
        var c = await CreateCaseAsync();
        var request = await _fixture.Repository.CreateSettlementAuthorityRequestAsync(c.Id, new CreateSettlementAuthorityRequest { RequestedAmount = 60_000m });

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _fixture.Repository.DecideSettlementAuthorityRequestAsync(request.Id, new DecideSettlementAuthorityRequest { Action = action, Comment = "   " }));

        var reloaded = await _fixture.Repository.GetSettlementAuthorityRequestsAsync(c.Id);
        Assert.Equal("Pending", Assert.Single(reloaded).Status);
    }

    [Fact]
    public async Task DecideAsync_UnrecognizedAction_ThrowsArgumentException()
    {
        var c = await CreateCaseAsync();
        var request = await _fixture.Repository.CreateSettlementAuthorityRequestAsync(c.Id, new CreateSettlementAuthorityRequest { RequestedAmount = 60_000m });

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _fixture.Repository.DecideSettlementAuthorityRequestAsync(request.Id, new DecideSettlementAuthorityRequest { Action = "Whatever", Comment = "A comment." }));
    }

    [Fact]
    public async Task DecideAsync_AlreadyApprovedRequest_ThrowsInvalidOperationException_AndDoesNotOverwriteCeilingAgain()
    {
        var c = await CreateCaseAsync();
        var request = await _fixture.Repository.CreateSettlementAuthorityRequestAsync(c.Id, new CreateSettlementAuthorityRequest { RequestedAmount = 60_000m });
        await _fixture.Repository.DecideSettlementAuthorityRequestAsync(request.Id, new DecideSettlementAuthorityRequest { Action = "Approved", Comment = "First decision." });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fixture.Repository.DecideSettlementAuthorityRequestAsync(request.Id, new DecideSettlementAuthorityRequest { Action = "Approved", Comment = "Second decision.", GrantedAmount = 99_000m }));
        Assert.Contains("already been decided", ex.Message);

        var reloadedCase = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == c.Id);
        Assert.Equal(60_000m, reloadedCase.SettlementAuthorizedCeiling);
    }

    [Fact]
    public async Task DecideAsync_AlreadyDeniedRequest_ThrowsInvalidOperationException()
    {
        var c = await CreateCaseAsync();
        var request = await _fixture.Repository.CreateSettlementAuthorityRequestAsync(c.Id, new CreateSettlementAuthorityRequest { RequestedAmount = 60_000m });
        await _fixture.Repository.DecideSettlementAuthorityRequestAsync(request.Id, new DecideSettlementAuthorityRequest { Action = "Denied", Comment = "First decision." });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fixture.Repository.DecideSettlementAuthorityRequestAsync(request.Id, new DecideSettlementAuthorityRequest { Action = "Approved", Comment = "Second decision." }));
    }

    [Fact]
    public async Task DecideAsync_UnknownRequestId_ThrowsInvalidOperationException()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fixture.Repository.DecideSettlementAuthorityRequestAsync(999_999, new DecideSettlementAuthorityRequest { Action = "Approved", Comment = "A comment." }));
    }

    // --- Trial-watch dashboard wiring: the previously-dead SettlementAuthority placeholder ---

    [Fact]
    public async Task TrialWatch_SurfacesGrantedCeiling_OnceACaseHasAnApprovedRequest()
    {
        var c = await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Trial Watch Settlement Authority Case",
            County = "Pulaski",
            Status = "Active",
            Track = "Contested",
            TrialTrack = true,
            DepositAmount = 100_000m,
        });
        var request = await _fixture.Repository.CreateSettlementAuthorityRequestAsync(c.Id, new CreateSettlementAuthorityRequest { RequestedAmount = 60_000m });
        await _fixture.Repository.DecideSettlementAuthorityRequestAsync(request.Id, new DecideSettlementAuthorityRequest { Action = "Approved", Comment = "Approved." });

        var dashboard = await _fixture.Repository.GetAttorneyDashboardAsync(new AttorneyDashboardFilters());

        var trialWatchEntry = Assert.Single(dashboard.TrialWatch, t => t.CaseName == "Trial Watch Settlement Authority Case");
        Assert.Equal(60_000m, trialWatchEntry.SettlementAuthority);
    }

    private sealed record RoleTestActor(Guid Id, string RoleLabel) : IApplicationActorContext
    {
        public Guid? UserId => Id;
        public string AuditLabel => RoleLabel;
        public string Role => RoleLabel;
    }
}
