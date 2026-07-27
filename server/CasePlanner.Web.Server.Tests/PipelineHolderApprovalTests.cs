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

    // --- Milestone 2's Filing Approval gate on the case-status transition itself
    // (PipelinePromotionGate.RequiresFilingApproval), exercised through
    // CasePlannerRepository.SaveCaseAsync -> SaveCaseInternalAsync. RequiresFilingApproval's trigger
    // condition is unchanged from Milestone 2 (still exercised by the two tests below, which don't
    // depend on WHICH check basis blocks the save). Milestone 4 corrected what actually gates a
    // Pipeline exit - it's no longer "Chief Counsel recorded an Approved decision in
    // pipeline_holder_approvals" but "the Director Signature Received milestone is marked in
    // case_prefiling_milestones" (PipelinePromotionGate.EnsureFilingReady) - see
    // PreFilingMilestoneTests.cs for that coverage now. ---

    [Fact]
    public async Task SaveCaseAsync_StayingInPipeline_UnrelatedFieldEditsNeverTriggerTheGate()
    {
        var c = await CreatePipelineCaseAsync();
        var loaded = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == c.Id);
        loaded.NextAction = "Draft the Complaint in Condemnation";
        loaded.ValuationNotes = "Unrelated field edit";
        // Explicitly re-saved as "Pipeline" (unchanged) alongside the unrelated edits.
        loaded.CaseStatus = "Pipeline";

        await _fixture.Repository.SaveCaseAsync(loaded);

        var reloaded = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == c.Id);
        Assert.Equal("Pipeline", reloaded.CaseStatus);
        Assert.Equal("Draft the Complaint in Condemnation", reloaded.NextAction);
        Assert.Equal("Unrelated field edit", reloaded.ValuationNotes);
    }

    [Fact]
    public async Task SaveCaseAsync_BrandNewCase_IsNeverGatedRegardlessOfApprovalState()
    {
        // Id == 0 - simulates an imported/backfilled case with no prior "Pipeline" state to leave,
        // so creation must always succeed here even with zero pipeline_holder_approvals rows.
        var c = await _fixture.Repository.SaveCaseAsync(new CaseRecord
        {
            CaseName = "Imported Case",
            County = "Pulaski",
            Status = "Active",
            Track = "Contested",
            Stage = "Service",
            CaseNumber = "1CV-24-500",
            CaseStatus = "Filed / Service Pending",
        });

        Assert.Equal("Filed / Service Pending", c.CaseStatus);
    }

    [Fact]
    public async Task SaveCaseAsync_AutoRecomputedCaseStatus_IsStillGatedEvenWhenIncomingCaseStatusIsBlank()
    {
        // The trickiest case: the incoming model's CaseStatus is left blank, which triggers
        // SaveCaseInternalAsync's existing auto-recompute block (MapConsolidatedCaseStatus) BEFORE
        // the gate check runs. Status="Active" (non-Pipeline), Stage="Service", and a non-blank
        // CaseNumber together make MapConsolidatedCaseStatus compute "Filed / Service Pending" -
        // the gate must observe THAT recomputed value, not the blank incoming one, otherwise a
        // save could dodge the gate simply by omitting CaseStatus from the request.
        var c = await CreatePipelineCaseAsync();
        var loaded = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == c.Id);
        loaded.CaseStatus = "";
        loaded.Status = "Active";
        loaded.Stage = "Service";
        loaded.CaseNumber = "1CV-24-777";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _fixture.Repository.SaveCaseAsync(loaded));
        Assert.Contains("Director signature", ex.Message);

        var reloaded = (await _fixture.Repository.GetCasesAsync("", "", "", "", true)).Single(x => x.Id == c.Id);
        Assert.Equal("Pipeline", reloaded.CaseStatus);
    }
}
