using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

// case_defendants child table, mirroring ICaseLegalAssistantStore's provider-selected store
// pattern (CaseLegalAssistantStores.cs) exactly.
public interface ICaseDefendantStore
{
    string Provider { get; }
    Task<List<CaseDefendantRecord>> GetAsync(long? caseId, CancellationToken token = default);
    Task<CaseDefendantRecord> SaveAsync(CaseDefendantRecord model, CancellationToken token = default);
    Task DeleteAsync(long id, string? rowVersion = null, CancellationToken token = default);
}

public sealed class SqliteCaseDefendantStore(CasePlannerRepository repository) : ICaseDefendantStore
{
    public string Provider => "Sqlite";
    public Task<List<CaseDefendantRecord>> GetAsync(long? caseId, CancellationToken token = default) => repository.GetCaseDefendantsAsync(caseId);
    public Task<CaseDefendantRecord> SaveAsync(CaseDefendantRecord model, CancellationToken token = default) => repository.SaveCaseDefendantAsync(model);
    public Task DeleteAsync(long id, string? rowVersion = null, CancellationToken token = default) => repository.DeleteCaseDefendantAsync(id);
}
