using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

public interface IOperationalWorkspaceQuery
{
    Task<CaseWorkspaceResponse?> GetWorkspaceAsync(
        long caseId,
        IReadOnlySet<long>? visibleCaseIds = null,
        CancellationToken token = default);

    Task<DashboardData> GetDashboardAsync(
        IReadOnlySet<long>? visibleCaseIds = null,
        CancellationToken token = default);

    Task<List<UpcomingWorkItemRecord>> GetUpcomingWorkAsync(
        string? type,
        string? urgency,
        int limit,
        IReadOnlySet<long>? visibleCaseIds = null,
        CancellationToken token = default);

    Task<List<ServiceQueueItem>> GetServiceQueueAsync(
        IReadOnlySet<long>? visibleCaseIds = null,
        CancellationToken token = default);

    Task<AttorneyDashboardResponse> GetAttorneyDashboardAsync(
        AttorneyDashboardFilters filters,
        IReadOnlySet<long>? visibleCaseIds = null,
        CancellationToken token = default);
}

public sealed class SqliteOperationalWorkspaceQuery(CasePlannerRepository repository) : IOperationalWorkspaceQuery
{
    public Task<CaseWorkspaceResponse?> GetWorkspaceAsync(
        long caseId,
        IReadOnlySet<long>? visibleCaseIds = null,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.GetCaseWorkspaceAsync(caseId, visibleCaseIds);
    }

    public Task<DashboardData> GetDashboardAsync(
        IReadOnlySet<long>? visibleCaseIds = null,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.GetDashboardAsync(visibleCaseIds);
    }

    public Task<List<UpcomingWorkItemRecord>> GetUpcomingWorkAsync(
        string? type,
        string? urgency,
        int limit,
        IReadOnlySet<long>? visibleCaseIds = null,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.GetUpcomingWorkAsync(type, urgency, limit, visibleCaseIds);
    }

    public Task<List<ServiceQueueItem>> GetServiceQueueAsync(
        IReadOnlySet<long>? visibleCaseIds = null,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.GetServiceQueueAsync(visibleCaseIds);
    }

    public Task<AttorneyDashboardResponse> GetAttorneyDashboardAsync(
        AttorneyDashboardFilters filters,
        IReadOnlySet<long>? visibleCaseIds = null,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.GetAttorneyDashboardAsync(filters, visibleCaseIds);
    }
}
