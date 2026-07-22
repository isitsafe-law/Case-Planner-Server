using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

// Test-build feedback item: case_legal_assistants child table, mirroring IOpposingAttorneyStore's
// provider-selected store pattern (OpposingAttorneyStores.cs) exactly.
public interface ICaseLegalAssistantStore
{
    string Provider { get; }
    Task<List<CaseLegalAssistantRecord>> GetAsync(long? caseId, CancellationToken token = default);
    Task<CaseLegalAssistantRecord> SaveAsync(CaseLegalAssistantRecord model, CancellationToken token = default);
    Task DeleteAsync(long id, string? rowVersion = null, CancellationToken token = default);
}

public sealed class SqliteCaseLegalAssistantStore(CasePlannerRepository repository) : ICaseLegalAssistantStore
{
    public string Provider => "Sqlite";
    public Task<List<CaseLegalAssistantRecord>> GetAsync(long? caseId, CancellationToken token = default) => repository.GetCaseLegalAssistantsAsync(caseId);
    public Task<CaseLegalAssistantRecord> SaveAsync(CaseLegalAssistantRecord model, CancellationToken token = default) => repository.SaveCaseLegalAssistantAsync(model);
    public Task DeleteAsync(long id, string? rowVersion = null, CancellationToken token = default) => repository.DeleteCaseLegalAssistantAsync(id);
}
