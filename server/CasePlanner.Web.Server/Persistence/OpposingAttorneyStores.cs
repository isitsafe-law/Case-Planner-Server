using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

// Item 1 (multi-user rollout Phase 2): case.opposingCounsel converted from a single free-text
// string to a one-to-many child table, mirroring the IWitnessStore/IPublicationEntryStore
// provider-selected store pattern used by every other simple per-case list in this app.
public interface IOpposingAttorneyStore
{
    string Provider { get; }
    Task<List<OpposingAttorneyRecord>> GetAsync(long? caseId, CancellationToken token = default);
    Task<OpposingAttorneyRecord> SaveAsync(OpposingAttorneyRecord model, CancellationToken token = default);
    Task DeleteAsync(long id, string? rowVersion = null, CancellationToken token = default);
}

public sealed class SqliteOpposingAttorneyStore(CasePlannerRepository repository) : IOpposingAttorneyStore
{
    public string Provider => "Sqlite";
    public Task<List<OpposingAttorneyRecord>> GetAsync(long? caseId, CancellationToken token = default) => repository.GetOpposingAttorneysAsync(caseId);
    public Task<OpposingAttorneyRecord> SaveAsync(OpposingAttorneyRecord model, CancellationToken token = default) => repository.SaveOpposingAttorneyAsync(model);
    public Task DeleteAsync(long id, string? rowVersion = null, CancellationToken token = default) => repository.DeleteOpposingAttorneyAsync(id);
}
