using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

// Manager/Administrator Dashboard Milestone 4 correction: replaces part of Milestone 2's Filing
// Approval gate with a plain record of ARDOT's real, out-of-band pre-filing sign-off process. See
// PreFilingMilestoneRecord's doc comment (DomainModels.cs) for the full rationale. Same
// provider-switched shape as ICircuitClerkStore/ISettlementAuthorityRequestStore: a plain
// interface, implemented once per provider, selected in Program.cs's DI registration by active
// database provider. Unlike PipelineHolderApprovalRecord (append-only), this updates in place - one
// row per (CaseId, Milestone), upserted on every mark/unmark.
public interface IPreFilingMilestoneStore
{
    string Provider { get; }
    Task<List<PreFilingMilestoneRecord>> GetAsync(long? caseId, CancellationToken token = default);
    Task<PreFilingMilestoneRecord> MarkAsync(long caseId, string milestone, MarkPreFilingMilestoneRequest request, CancellationToken token = default);
    Task<PreFilingMilestoneRecord> UnmarkAsync(long caseId, string milestone, UnmarkPreFilingMilestoneRequest request, CancellationToken token = default);
    // Data contract for GET /api/prefiling-milestones/aging - see PreFilingMilestoneAgingSummary's
    // doc comment (DomainModels.cs).
    Task<PreFilingMilestoneAgingSummary> GetAgingAsync(CancellationToken token = default);
}

public sealed class SqlitePreFilingMilestoneStore(CasePlannerRepository repository) : IPreFilingMilestoneStore
{
    public string Provider => "Sqlite";
    public Task<List<PreFilingMilestoneRecord>> GetAsync(long? caseId, CancellationToken token = default) =>
        repository.GetPreFilingMilestonesAsync(caseId);
    public Task<PreFilingMilestoneRecord> MarkAsync(long caseId, string milestone, MarkPreFilingMilestoneRequest request, CancellationToken token = default) =>
        repository.MarkPreFilingMilestoneAsync(caseId, milestone, request);
    public Task<PreFilingMilestoneRecord> UnmarkAsync(long caseId, string milestone, UnmarkPreFilingMilestoneRequest request, CancellationToken token = default) =>
        repository.UnmarkPreFilingMilestoneAsync(caseId, milestone, request);
    public Task<PreFilingMilestoneAgingSummary> GetAgingAsync(CancellationToken token = default) =>
        repository.GetPreFilingMilestoneAgingAsync();
}
