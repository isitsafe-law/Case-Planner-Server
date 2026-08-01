using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

public interface IReportDefinitionStore
{
    Task<List<SavedReportDefinition>> GetAsync(CancellationToken token = default);
    Task<SavedReportDefinition> SaveAsync(SaveReportDefinitionRequest request, CancellationToken token = default);
    Task<bool> DeleteAsync(string id, CancellationToken token = default);
}

public sealed class SqliteReportDefinitionStore(CasePlannerRepository repository) : IReportDefinitionStore
{
    public Task<List<SavedReportDefinition>> GetAsync(CancellationToken token = default) => repository.GetSavedReportDefinitionsAsync();
    public Task<SavedReportDefinition> SaveAsync(SaveReportDefinitionRequest request, CancellationToken token = default) => repository.SaveReportDefinitionAsync(request);
    public Task<bool> DeleteAsync(string id, CancellationToken token = default) => repository.DeleteReportDefinitionAsync(id);
}
