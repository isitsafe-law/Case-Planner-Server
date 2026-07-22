using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

// Staff Directory (attorneys/legal_assistants) previously had no provider-switched store - every
// /api/staff-directory/* endpoint called the SQLite CasePlannerRepository singleton directly, so
// writes always landed in SQLite even when Database:ActiveProvider was SqlServer. That silently
// broke the linked_user_id notification-resolution feature (042_staff_directory_linked_user.sql),
// which queries dbo.attorneys/dbo.legal_assistants on SQL Server - tables that would stay
// permanently empty under the old wiring. These interfaces close that gap, mirroring
// ICaseLegalAssistantStore's provider-selected pattern (CaseLegalAssistantStores.cs) exactly.
public interface IAttorneyStore
{
    string Provider { get; }
    Task<List<AttorneyRecord>> GetAsync(CancellationToken token = default);
    Task<AttorneyRecord> SaveAsync(AttorneyRecord model, CancellationToken token = default);
}

public sealed class SqliteAttorneyStore(CasePlannerRepository repository) : IAttorneyStore
{
    public string Provider => "Sqlite";
    public Task<List<AttorneyRecord>> GetAsync(CancellationToken token = default) => repository.GetAttorneysAsync();
    public Task<AttorneyRecord> SaveAsync(AttorneyRecord model, CancellationToken token = default) => repository.SaveAttorneyAsync(model);
}

public interface ILegalAssistantStore
{
    string Provider { get; }
    Task<List<LegalAssistantRecord>> GetAsync(CancellationToken token = default);
    Task<LegalAssistantRecord> SaveAsync(LegalAssistantRecord model, CancellationToken token = default);
}

public sealed class SqliteLegalAssistantStore(CasePlannerRepository repository) : ILegalAssistantStore
{
    public string Provider => "Sqlite";
    public Task<List<LegalAssistantRecord>> GetAsync(CancellationToken token = default) => repository.GetLegalAssistantsAsync();
    public Task<LegalAssistantRecord> SaveAsync(LegalAssistantRecord model, CancellationToken token = default) => repository.SaveLegalAssistantAsync(model);
}
