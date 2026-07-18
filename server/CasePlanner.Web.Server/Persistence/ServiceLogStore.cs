using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

public interface IServiceLogStore
{
    Task<List<ServiceLogEntry>> GetAsync(long caseId, CancellationToken token = default);
    Task<ServiceLogEntry> SaveAsync(ServiceLogEntry model, CancellationToken token = default);
    Task DeleteAsync(long id, CancellationToken token = default);
}

public sealed class SqliteServiceLogStore(CasePlannerRepository repository) : IServiceLogStore
{
    public Task<List<ServiceLogEntry>> GetAsync(long caseId, CancellationToken token = default) =>
        repository.GetServiceLogEntriesAsync(caseId);

    public Task<ServiceLogEntry> SaveAsync(ServiceLogEntry model, CancellationToken token = default) =>
        repository.SaveServiceLogEntryAsync(model);

    public Task DeleteAsync(long id, CancellationToken token = default) =>
        repository.DeleteServiceLogEntryAsync(id);
}

public sealed class SqlServerServiceLogStore : IServiceLogStore
{
    private const string Message = "Per-party service log SQL Server implementation is not built yet - SQLite-only pending SQL Server sandbox access, matching the unified document platform's precedent.";

    public Task<List<ServiceLogEntry>> GetAsync(long caseId, CancellationToken token = default) =>
        throw new NotSupportedException(Message);

    public Task<ServiceLogEntry> SaveAsync(ServiceLogEntry model, CancellationToken token = default) =>
        throw new NotSupportedException(Message);

    public Task DeleteAsync(long id, CancellationToken token = default) =>
        throw new NotSupportedException(Message);
}
