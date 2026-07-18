using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

public interface IOrganizationDefaultsStore
{
    string Provider { get; }
    Task<OrgDefaults> GetAsync(CancellationToken token = default);
    Task<OrgDefaults> SaveAsync(OrgDefaults model, CancellationToken token = default);
}

public interface IWorkTemplateAdministration
{
    string Provider { get; }
    Task<List<ChecklistTemplateRecord>> GetChecklistAsync(CancellationToken token = default);
    Task<ChecklistTemplateRecord> SaveChecklistAsync(ChecklistTemplateRecord model, CancellationToken token = default);
    Task<ChecklistTemplateItemRecord> SaveChecklistItemAsync(ChecklistTemplateItemRecord model, CancellationToken token = default);
    Task DeleteChecklistAsync(long id, string? rowVersion, CancellationToken token = default);
    Task DeleteChecklistItemAsync(long id, string? rowVersion, CancellationToken token = default);
    Task<List<DeadlineTemplateRecord>> GetDeadlinesAsync(CancellationToken token = default);
    Task<DeadlineTemplateRecord> SaveDeadlineAsync(DeadlineTemplateRecord model, CancellationToken token = default);
}

public sealed class SqliteOrganizationDefaultsStore(CasePlannerRepository repository) : IOrganizationDefaultsStore
{
    public string Provider => "Sqlite";
    public Task<OrgDefaults> GetAsync(CancellationToken token = default) => repository.GetOrgDefaultsAsync();
    public Task<OrgDefaults> SaveAsync(OrgDefaults model, CancellationToken token = default) => repository.SaveOrgDefaultsAsync(model);
}

public sealed class SqlServerOrganizationDefaultsAdministration(SqlServerOrganizationDefaultsStore store) : IOrganizationDefaultsStore
{
    public string Provider => "SqlServer";
    public Task<OrgDefaults> GetAsync(CancellationToken token = default) => store.GetAsync(token);
    public Task<OrgDefaults> SaveAsync(OrgDefaults model, CancellationToken token = default) => store.SaveAsync(model, token);
}

public sealed class SqliteWorkTemplateAdministration(CasePlannerRepository repository) : IWorkTemplateAdministration
{
    public string Provider => "Sqlite";
    public Task<List<ChecklistTemplateRecord>> GetChecklistAsync(CancellationToken token = default) => repository.GetChecklistTemplatesAsync();
    public Task<ChecklistTemplateRecord> SaveChecklistAsync(ChecklistTemplateRecord model, CancellationToken token = default) => repository.SaveChecklistTemplateAsync(model);
    public Task<ChecklistTemplateItemRecord> SaveChecklistItemAsync(ChecklistTemplateItemRecord model, CancellationToken token = default) => repository.SaveChecklistTemplateItemAsync(model);
    public Task DeleteChecklistAsync(long id, string? rowVersion, CancellationToken token = default) => repository.DeleteChecklistTemplateAsync(id);
    public Task DeleteChecklistItemAsync(long id, string? rowVersion, CancellationToken token = default) => repository.DeleteChecklistTemplateItemAsync(id);
    public Task<List<DeadlineTemplateRecord>> GetDeadlinesAsync(CancellationToken token = default) => repository.GetDeadlineTemplatesAsync();
    public Task<DeadlineTemplateRecord> SaveDeadlineAsync(DeadlineTemplateRecord model, CancellationToken token = default) => repository.SaveDeadlineTemplateAsync(model);
}

public sealed class SqlServerWorkTemplateAdministration(SqlServerWorkTemplateStore store) : IWorkTemplateAdministration
{
    public string Provider => "SqlServer";
    public Task<List<ChecklistTemplateRecord>> GetChecklistAsync(CancellationToken token = default) => store.GetChecklistAsync(token);
    public Task<ChecklistTemplateRecord> SaveChecklistAsync(ChecklistTemplateRecord model, CancellationToken token = default) => store.SaveChecklistAsync(model, token);
    public Task<ChecklistTemplateItemRecord> SaveChecklistItemAsync(ChecklistTemplateItemRecord model, CancellationToken token = default) => store.SaveChecklistItemAsync(model, token);
    public Task DeleteChecklistAsync(long id, string? rowVersion, CancellationToken token = default) => store.DeleteChecklistAsync(id, rowVersion, token);
    public Task DeleteChecklistItemAsync(long id, string? rowVersion, CancellationToken token = default) => store.DeleteChecklistItemAsync(id, rowVersion, token);
    public Task<List<DeadlineTemplateRecord>> GetDeadlinesAsync(CancellationToken token = default) => store.GetDeadlinesAsync(token);
    public Task<DeadlineTemplateRecord> SaveDeadlineAsync(DeadlineTemplateRecord model, CancellationToken token = default) => store.SaveDeadlineAsync(model, token);
}
