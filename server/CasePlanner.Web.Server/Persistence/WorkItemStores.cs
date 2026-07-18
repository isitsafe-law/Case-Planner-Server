using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

public interface IDeadlineStore
{
    string Provider { get; }
    Task<List<DeadlineItem>> GetAsync(long? caseId, CancellationToken token = default);
    Task<DeadlineItem> SaveAsync(DeadlineItem model, CancellationToken token = default);
    Task DeleteAsync(long id, string? rowVersion = null, CancellationToken token = default);
}

public interface IChecklistStore
{
    string Provider { get; }
    Task<List<ChecklistItemRecord>> GetAsync(long? caseId, CancellationToken token = default);
    Task<ChecklistItemRecord> SaveAsync(ChecklistItemRecord model, CancellationToken token = default);
    Task DeleteAsync(long id, string? rowVersion = null, CancellationToken token = default);
}

public interface IDiscoveryTrackingStore
{
    string Provider { get; }
    Task<List<DiscoveryItemRecord>> GetAsync(long? caseId, CancellationToken token = default);
    Task<DiscoveryItemRecord> SaveAsync(DiscoveryItemRecord model, CancellationToken token = default);
}

public sealed class SqliteDeadlineStore(CasePlannerRepository repository) : IDeadlineStore
{
    public string Provider => "Sqlite";
    public Task<List<DeadlineItem>> GetAsync(long? caseId, CancellationToken token = default) => repository.GetDeadlinesAsync(caseId);
    public Task<DeadlineItem> SaveAsync(DeadlineItem model, CancellationToken token = default) => repository.SaveDeadlineAsync(model);
    public Task DeleteAsync(long id, string? rowVersion = null, CancellationToken token = default) => repository.DeleteDeadlineAsync(id);
}

public sealed class SqliteChecklistStore(CasePlannerRepository repository) : IChecklistStore
{
    public string Provider => "Sqlite";
    public Task<List<ChecklistItemRecord>> GetAsync(long? caseId, CancellationToken token = default) => repository.GetChecklistItemsAsync(caseId);
    public Task<ChecklistItemRecord> SaveAsync(ChecklistItemRecord model, CancellationToken token = default) => repository.SaveChecklistItemAsync(model);
    public Task DeleteAsync(long id, string? rowVersion = null, CancellationToken token = default) => repository.DeleteChecklistItemAsync(id);
}

public sealed class SqliteDiscoveryTrackingStore(CasePlannerRepository repository) : IDiscoveryTrackingStore
{
    public string Provider => "Sqlite";
    public Task<List<DiscoveryItemRecord>> GetAsync(long? caseId, CancellationToken token = default) => repository.GetDiscoveryItemsAsync(caseId);
    public Task<DiscoveryItemRecord> SaveAsync(DiscoveryItemRecord model, CancellationToken token = default) => repository.SaveDiscoveryItemAsync(model);
}
