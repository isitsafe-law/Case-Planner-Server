using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

// Manager/Administrator Dashboard Milestone 3: the Settlement Authority workflow - a request for
// authority to settle up to a given amount. Manager Dashboard sign-off consolidation, item 4: this
// is pure record-keeping now - recording any outcome (Approved/Denied/InfoRequested) requires only
// ordinary case-write access (see Program.cs's endpoint mapping), not a specific role; the former
// Chief-Counsel-exclusive gate and the "no amount threshold" framing are gone along with it, since
// there is no routing/threshold/escalation logic left to describe. Same provider-switched shape as
// ICircuitClerkStore/IPipelineHolderApprovalStore: a plain interface, implemented once per
// provider, selected in Program.cs's DI registration by active database provider. Unlike
// PipelineHolderApprovalRecord (append-only), this updates in place - see
// SettlementAuthorityRequestRecord's doc comment.
public interface ISettlementAuthorityRequestStore
{
    string Provider { get; }
    Task<List<SettlementAuthorityRequestRecord>> GetAsync(long? caseId, CancellationToken token = default);
    Task<SettlementAuthorityRequestRecord> CreateAsync(long caseId, CreateSettlementAuthorityRequest request, CancellationToken token = default);
    Task<SettlementAuthorityRequestRecord> DecideAsync(long requestId, DecideSettlementAuthorityRequest decision, CancellationToken token = default);
}

public sealed class SqliteSettlementAuthorityRequestStore(CasePlannerRepository repository) : ISettlementAuthorityRequestStore
{
    public string Provider => "Sqlite";
    public Task<List<SettlementAuthorityRequestRecord>> GetAsync(long? caseId, CancellationToken token = default) =>
        repository.GetSettlementAuthorityRequestsAsync(caseId);
    public Task<SettlementAuthorityRequestRecord> CreateAsync(long caseId, CreateSettlementAuthorityRequest request, CancellationToken token = default) =>
        repository.CreateSettlementAuthorityRequestAsync(caseId, request);
    public Task<SettlementAuthorityRequestRecord> DecideAsync(long requestId, DecideSettlementAuthorityRequest decision, CancellationToken token = default) =>
        repository.DecideSettlementAuthorityRequestAsync(requestId, decision);
}
