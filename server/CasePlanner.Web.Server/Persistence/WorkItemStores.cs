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
    Task DeleteAsync(long id, string? rowVersion = null, CancellationToken token = default);
}

// Multi-user rollout Phase 4a (notifications core). CreateAsync is the "shared insert" - it just
// takes recipient ids and text, with no knowledge of where those ids came from (checklist
// assignment, case_assignments' Attorney role, or a test calling it directly).
public interface INotificationStore
{
    string Provider { get; }
    Task CreateAsync(IReadOnlyCollection<string> recipientUserIds, string notificationType, long? caseId, string title, string body, CancellationToken token = default);
    Task<NotificationFeed> GetForRecipientAsync(string recipientUserId, int limit = 50, CancellationToken token = default);
    Task<bool> MarkReadAsync(long id, string recipientUserId, CancellationToken token = default);
    Task MarkAllReadAsync(string recipientUserId, CancellationToken token = default);
}

// Multi-user rollout Phase 4c (per-user notification preferences). Separate from INotificationStore
// above - preferences are a distinct concern (per-user settings) from notification creation/reading,
// and this is fully dual-provider testable (no case_assignments dependency), unlike some of the rest
// of this rollout's SQL-Server-only pieces.
public interface INotificationPreferencesStore
{
    string Provider { get; }
    Task<NotificationPreferencesRecord> GetAsync(string userId, CancellationToken token = default);
    Task<NotificationPreferencesRecord> UpsertAsync(NotificationPreferencesRecord preferences, CancellationToken token = default);
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
    public Task DeleteAsync(long id, string? rowVersion = null, CancellationToken token = default) => repository.DeleteDiscoveryItemAsync(id);
}

// Phase 4b (email delivery): deliberately sends no email. Email requires a real address, resolvable
// only from app_users.email (SQL Server only - see SqlServerNotificationStore.CreateAsync); SQLite
// has no such table, so there is nothing to look up here. Not a gap, just the correct behavior for
// this provider - same "inert locally" pattern as the rest of this rollout's SQL-Server-only pieces.
public sealed class SqliteNotificationStore(CasePlannerRepository repository) : INotificationStore
{
    public string Provider => "Sqlite";
    public Task CreateAsync(IReadOnlyCollection<string> recipientUserIds, string notificationType, long? caseId, string title, string body, CancellationToken token = default) =>
        repository.CreateNotificationsAsync(recipientUserIds, notificationType, caseId, title, body);
    public Task<NotificationFeed> GetForRecipientAsync(string recipientUserId, int limit = 50, CancellationToken token = default) =>
        repository.GetNotificationsForRecipientAsync(recipientUserId, limit);
    public Task<bool> MarkReadAsync(long id, string recipientUserId, CancellationToken token = default) =>
        repository.MarkNotificationReadAsync(id, recipientUserId);
    public Task MarkAllReadAsync(string recipientUserId, CancellationToken token = default) =>
        repository.MarkAllNotificationsReadAsync(recipientUserId);
}

public sealed class SqliteNotificationPreferencesStore(CasePlannerRepository repository) : INotificationPreferencesStore
{
    public string Provider => "Sqlite";
    public Task<NotificationPreferencesRecord> GetAsync(string userId, CancellationToken token = default) =>
        repository.GetNotificationPreferencesAsync(userId);
    public Task<NotificationPreferencesRecord> UpsertAsync(NotificationPreferencesRecord preferences, CancellationToken token = default) =>
        repository.UpsertNotificationPreferencesAsync(preferences);
}
