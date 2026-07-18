using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

public sealed class SqliteCaseCatalogReader(CasePlannerRepository repository) : ICaseCatalogStore
{
    public string Provider => "Sqlite";

    public Task<List<CaseRecord>> GetCasesAsync(CaseCatalogQuery query, CancellationToken cancellationToken = default) =>
        repository.GetCasesAsync(query.Search, query.Status, query.County, query.Stage, query.IncludeClosed, query.Track, query.CaseStatus, query.DateOpenedFrom, query.DateOpenedTo, query.DateClosedFrom, query.DateClosedTo);

    public Task<CaseRecord> SaveCaseAsync(CaseRecord model, CancellationToken cancellationToken = default) =>
        repository.SaveCaseAsync(model);

    public Task DeleteCaseAsync(long caseId, string? rowVersion = null, CancellationToken cancellationToken = default) =>
        repository.DeleteCaseAsync(caseId);
}
