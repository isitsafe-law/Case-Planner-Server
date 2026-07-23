using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

// County Tax Collector reference lookup - same provider-switched shape as ICircuitClerkStore
// (CircuitClerkStores.cs): a fixed, independent reference table with zero auth/identity
// dependency. See CollectorRecord for the full rationale, including why Name is nullable here.
public interface ICollectorStore
{
    string Provider { get; }
    Task<List<CollectorRecord>> GetAsync(CancellationToken token = default);
    Task<CollectorRecord> SaveAsync(CollectorRecord model, CancellationToken token = default);
}

public sealed class SqliteCollectorStore(CasePlannerRepository repository) : ICollectorStore
{
    public string Provider => "Sqlite";
    public Task<List<CollectorRecord>> GetAsync(CancellationToken token = default) => repository.GetCollectorsAsync();
    public Task<CollectorRecord> SaveAsync(CollectorRecord model, CancellationToken token = default) => repository.SaveCollectorAsync(model);
}
