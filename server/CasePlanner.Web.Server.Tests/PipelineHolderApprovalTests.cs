using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Persistence;
using CasePlanner.Web.Server.Security;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Tests;

// Covers the pipeline-advancement gate (PipelinePromotionGate / CasePlannerRepository.SetHolderAsync)
// and the Approve / Return for Revision action (ProviderNeutralPipelineHolderApprovalActionService).
// Mirrors CaseDefendantTests's structure - a fresh RepositoryTestFixture per test, plain assertions
// against the real SQLite repository (no mocking).
public class PipelineHolderApprovalTests : IAsyncLifetime
{
    private RepositoryTestFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await RepositoryTestFixture.CreateAsync();
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    // Status="Pipeline" is required so SaveCaseAsync's MapConsolidatedCaseStatus derives
    // CaseStatus="Pipeline" (the phase PipelinePromotionGate actually gates) rather than the
    // "Active Litigation" bucket CaseDefendantTests's plain "Active" helper case lands in.
    private async Task<CaseRecord> CreatePipelineCaseAsync(string currentHolder = "Legal Assistant") =>
        await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Fixture Pipeline Case",
            County = "Pulaski",
            Status = "Pipeline",
            Track = "Contested",
            CurrentHolder = currentHolder,
        });

    private IPipelineHolderApprovalActionService BuildActionService(IApplicationActorContext? actor = null)
    {
        var repository = _fixture.Repository;
        return new ProviderNeutralPipelineHolderApprovalActionService(
            new SqlitePipelineHolderApprovalStore(repository),
            new SqliteCaseQuickActionService(repository),
            new SqliteCaseCatalogReader(repository),
            actor ?? new LocalApplicationActorContext());
    }

    // --- Task A: raw append-only storage ---

    [Fact]
    public async Task RecordAndGetPipelineHolderApprovals_RoundTripsAndOrdersMostRecentFirst()
    {
        var c = await CreatePipelineCaseAsync();

        var first = await _fixture.Repository.RecordPipelineHolderApprovalAsync(new PipelineHolderApprovalRecord
        {
            CaseId = c.Id, HolderRole = "Legal Assistant", Status = "Approved", Note = "Ready for attorney review.", SetByDisplayName = "Jane LA",
        });
        var second = await _fixture.Repository.RecordPipelineHolderApprovalAsync(new PipelineHolderApprovalRecord
        {
            CaseId = c.Id, HolderRole = "Attorney", Status = "Returned", Note = "Needs a revised legal description.",
        });

        Assert.NotEqual(0, first.Id);
        Assert.NotEqual(0, second.Id);
        Assert.NotEmpty(first.SetAt);

        var list = await _fixture.Repository.GetPipelineHolderApprovalsAsync(c.Id);
        Assert.Equal(2, list.Count);
        // Most recent (highest id) first.
        Assert.Equal(second.Id, list[0].Id);
        Assert.Equal("Attorney", list[0].HolderRole);
        Assert.Equal("Returned", list[0].Status);
        Assert.Equal("Legal Assistant", list[1].HolderRole);
        Assert.Equal("Approved", list[1].Status);
        Assert.Equal("Jane LA", list[1].SetByDisplayName);
        Assert.Equal("Ready for attorney review.", list[1].Note);
    }

    // --- Task B: the gate itself, exercised directly through SetHolderAsync ---

    [Fact]
    public async Task SetHolderAsync_ForwardAdvance_BlockedWithoutPriorApprovedRow()
    {
        var c = await CreatePipelineCaseAsync("Legal Assistant");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fixture.Repository.SetHolderAsync(c.Id, new SetHolderRequest { CurrentHolder = "Attorney" }));
        Assert.Contains("Legal Assistant", ex.Message);
        Assert.Contains("Attorney", ex.Message);

        var reloaded = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == c.Id);
        Assert.Equal("Legal Assistant", reloaded.CurrentHolder);
    }

    [Fact]
    public async Task SetHolderAsync_ForwardAdvance_SucceedsOnceThePriorHolderApproved()
    {
        var c = await CreatePipelineCaseAsync("Legal Assistant");
        await _fixture.Repository.RecordPipelineHolderApprovalAsync(new PipelineHolderApprovalRecord
        {
            CaseId = c.Id, HolderRole = "Legal Assistant", Status = "Approved",
        });

        await _fixture.Repository.SetHolderAsync(c.Id, new SetHolderRequest { CurrentHolder = "Attorney" });

        var reloaded = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == c.Id);
        Assert.Equal("Attorney", reloaded.CurrentHolder);
    }

    [Fact]
    public async Task SetHolderAsync_ForwardAdvance_BlockedWhenMostRecentRowForThatHolderWasReturned()
    {
        var c = await CreatePipelineCaseAsync("Legal Assistant");
        // An older Approved row exists, but the most recent status for this holder is Returned -
        // the gate must key off the latest row, not "any Approved row ever".
        await _fixture.Repository.RecordPipelineHolderApprovalAsync(new PipelineHolderApprovalRecord
        {
            CaseId = c.Id, HolderRole = "Legal Assistant", Status = "Approved",
        });
        await _fixture.Repository.RecordPipelineHolderApprovalAsync(new PipelineHolderApprovalRecord
        {
            CaseId = c.Id, HolderRole = "Legal Assistant", Status = "Returned",
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _fixture.Repository.SetHolderAsync(c.Id, new SetHolderRequest { CurrentHolder = "Attorney" }));
    }

    [Fact]
    public async Task SetHolderAsync_BackwardOrLateralMove_IsNeverBlockedByTheGate()
    {
        // Starts at the far end of the chain with zero approval rows on file at all - a legitimate
        // forward advance from here would be blocked, but Return for Revision (backward) must not be.
        var c = await CreatePipelineCaseAsync("Chief Counsel");

        await _fixture.Repository.SetHolderAsync(c.Id, new SetHolderRequest { CurrentHolder = "Attorney" });

        var reloaded = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == c.Id);
        Assert.Equal("Attorney", reloaded.CurrentHolder);
    }

    [Fact]
    public async Task SetHolderAsync_LateralMoveToSameHolder_IsNeverBlockedByTheGate()
    {
        var c = await CreatePipelineCaseAsync("Attorney");

        await _fixture.Repository.SetHolderAsync(c.Id, new SetHolderRequest { CurrentHolder = "Attorney" });

        var reloaded = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == c.Id);
        Assert.Equal("Attorney", reloaded.CurrentHolder);
    }

    [Fact]
    public async Task SetHolderAsync_OutsidePipelinePhase_GateIsANoOp()
    {
        var c = await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Filed Case", County = "Pulaski", Status = "Active", Track = "Contested", CurrentHolder = "Legal Assistant",
        });
        Assert.NotEqual("Pipeline", c.CaseStatus); // sanity check on the fixture assumption

        await _fixture.Repository.SetHolderAsync(c.Id, new SetHolderRequest { CurrentHolder = "Attorney" });

        var reloaded = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == c.Id);
        Assert.Equal("Attorney", reloaded.CurrentHolder);
    }

    [Fact]
    public async Task SetHolderAsync_UngatedRoleOnEitherSide_GateIsANoOp()
    {
        // Moving from an ungated role ("Filing Staff") forward into a gated one.
        var fromUngated = await CreatePipelineCaseAsync("Filing Staff");
        await _fixture.Repository.SetHolderAsync(fromUngated.Id, new SetHolderRequest { CurrentHolder = "Attorney" });
        var reloadedFromUngated = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == fromUngated.Id);
        Assert.Equal("Attorney", reloadedFromUngated.CurrentHolder);

        // Moving from a gated role into an ungated one ("Other"), with no approval on file.
        var toUngated = await CreatePipelineCaseAsync("Chief Counsel");
        await _fixture.Repository.SetHolderAsync(toUngated.Id, new SetHolderRequest { CurrentHolder = "Other" });
        var reloadedToUngated = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == toUngated.Id);
        Assert.Equal("Other", reloadedToUngated.CurrentHolder);
    }

    // --- Task C: the Approve / Return for Revision action ---

    [Fact]
    public async Task RecordAsync_ChiefCounselApproved_AutoPopulatesWaitingFields()
    {
        var c = await CreatePipelineCaseAsync("Chief Counsel");
        var service = BuildActionService();

        await service.RecordAsync(c.Id, new RecordPipelineHolderApprovalRequest { HolderRole = "Chief Counsel", Status = "Approved" });

        var reloaded = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == c.Id);
        Assert.Equal("Director of Highways and Transportation — Declaration of Taking signature", reloaded.WaitingOn);
        Assert.Equal(DateTime.UtcNow.ToString("yyyy-MM-dd"), reloaded.WaitingStartedDate);
        // Not itself an advance - the office process treats this as a wait for a signature, not a
        // rejection or a further stepper move, so CurrentHolder is untouched.
        Assert.Equal("Chief Counsel", reloaded.CurrentHolder);
    }

    [Fact]
    public async Task RecordAsync_NonChiefCounselApproved_DoesNotTouchWaitingFields()
    {
        var c = await CreatePipelineCaseAsync("Attorney");
        var service = BuildActionService();

        await service.RecordAsync(c.Id, new RecordPipelineHolderApprovalRequest { HolderRole = "Attorney", Status = "Approved" });

        var reloaded = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == c.Id);
        Assert.Null(reloaded.WaitingOn);
        Assert.Null(reloaded.WaitingStartedDate);
        Assert.Equal("Attorney", reloaded.CurrentHolder);
    }

    [Fact]
    public async Task RecordAsync_Returned_MovesTheCaseBackToThePriorHolderInTheChain()
    {
        var c = await CreatePipelineCaseAsync("Deputy Chief Counsel");
        var service = BuildActionService();

        await service.RecordAsync(c.Id, new RecordPipelineHolderApprovalRequest { HolderRole = "Deputy Chief Counsel", Status = "Returned", Note = "Needs another valuation exhibit." });

        var reloaded = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == c.Id);
        Assert.Equal("Attorney", reloaded.CurrentHolder);

        var approvals = await _fixture.Repository.GetPipelineHolderApprovalsAsync(c.Id);
        var logged = Assert.Single(approvals);
        Assert.Equal("Deputy Chief Counsel", logged.HolderRole);
        Assert.Equal("Returned", logged.Status);
        Assert.Equal("Needs another valuation exhibit.", logged.Note);
    }

    [Fact]
    public async Task RecordAsync_UnrecognizedHolderRole_ThrowsArgumentException()
    {
        var c = await CreatePipelineCaseAsync("Attorney");
        var service = BuildActionService();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RecordAsync(c.Id, new RecordPipelineHolderApprovalRequest { HolderRole = "Filing Staff", Status = "Approved" }));
    }
}
