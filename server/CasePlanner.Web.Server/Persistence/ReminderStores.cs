using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

// SQLite-only, matching IPrefilingReviewStore's precedent for this class of append-only
// case-workflow-event feature - no SQL Server pilot store exists for this table.
public interface IReminderStore
{
    string Provider { get; }
    Task<List<ReminderRequestRecord>> GetRequestsAsync(long? caseId, CancellationToken token = default);
    Task<List<ReminderRequestRecord>> GetOpenAsync(CancellationToken token = default);
    Task<ReminderRequestRecord> RequestOrFollowUpAsync(long caseId, RequestAttorneyReminderRequest request, CancellationToken token = default);
    Task<ReminderRequestRecord> ResolveAsync(long caseId, ResolveReminderRequest request, CancellationToken token = default);
}

public sealed class SqliteReminderStore(CasePlannerRepository repository) : IReminderStore
{
    public string Provider => "Sqlite";
    public Task<List<ReminderRequestRecord>> GetRequestsAsync(long? caseId, CancellationToken token = default) => repository.GetReminderRequestsAsync(caseId);
    public Task<List<ReminderRequestRecord>> GetOpenAsync(CancellationToken token = default) => repository.GetOpenAttorneyRemindersAsync();
    public Task<ReminderRequestRecord> RequestOrFollowUpAsync(long caseId, RequestAttorneyReminderRequest request, CancellationToken token = default) => repository.RequestOrFollowUpAttorneyReminderAsync(caseId, request);
    public Task<ReminderRequestRecord> ResolveAsync(long caseId, ResolveReminderRequest request, CancellationToken token = default) => repository.ResolveReminderAsync(caseId, request);
}
