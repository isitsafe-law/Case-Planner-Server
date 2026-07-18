using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Persistence;

public sealed record CaseCatalogQuery(
    string Search = "",
    string Status = "",
    string County = "",
    string Stage = "",
    bool IncludeClosed = false,
    string Track = "",
    string CaseStatus = "",
    string DateOpenedFrom = "",
    string DateOpenedTo = "",
    string DateClosedFrom = "",
    string DateClosedTo = "");

public interface ICaseCatalogReader
{
    string Provider { get; }
    Task<List<CaseRecord>> GetCasesAsync(CaseCatalogQuery query, CancellationToken cancellationToken = default);
}

public interface ICaseCatalogStore : ICaseCatalogReader
{
    Task<CaseRecord> SaveCaseAsync(CaseRecord model, CancellationToken cancellationToken = default);
    Task DeleteCaseAsync(long caseId, string? rowVersion = null, CancellationToken cancellationToken = default);
}
