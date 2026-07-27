using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

// Manager/Administrator Dashboard Milestone 3: the Settlement Authority workflow - a request for
// authority to settle up to a given amount, decided EXCLUSIVELY by Chief Counsel (no amount
// threshold, no Deputy Chief Counsel action rights, no Administrator override - already decided
// with the user, and stricter than every other admin-gated action in this app). Same
// provider-switched shape as ICircuitClerkStore/IPipelineHolderApprovalStore: a plain interface,
// implemented once per provider, selected in Program.cs's DI registration by active database
// provider. Unlike PipelineHolderApprovalRecord (append-only), this updates in place - see
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
