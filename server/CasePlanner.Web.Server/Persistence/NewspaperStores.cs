using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

// Newspaper of general circulation reference lookup (final implementation, item 7). Same
// provider-switched shape as ICollectorStore/ICircuitClerkStore, but unlike those, this is true
// per-row CRUD keyed by Id - a county can have multiple newspapers, so there is no "resolve by
// county" upsert step. See NewspaperRecord for the full rationale.
public interface INewspaperStore
{
    string Provider { get; }
    Task<List<NewspaperRecord>> GetAsync(CancellationToken token = default);
    Task<NewspaperRecord> SaveAsync(NewspaperRecord model, CancellationToken token = default);
}

public sealed class SqliteNewspaperStore(CasePlannerRepository repository) : INewspaperStore
{
    public string Provider => "Sqlite";
    public Task<List<NewspaperRecord>> GetAsync(CancellationToken token = default) => repository.GetNewspapersAsync();
    public Task<NewspaperRecord> SaveAsync(NewspaperRecord model, CancellationToken token = default) => repository.SaveNewspaperAsync(model);
}
