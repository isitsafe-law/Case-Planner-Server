using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

// County Assessor reference lookup - same provider-switched shape as ICircuitClerkStore
// (CircuitClerkStores.cs): a fixed, independent reference table with zero auth/identity
// dependency. See AssessorRecord for the full rationale.
public interface IAssessorStore
{
    string Provider { get; }
    Task<List<AssessorRecord>> GetAsync(CancellationToken token = default);
    Task<AssessorRecord> SaveAsync(AssessorRecord model, CancellationToken token = default);
}

public sealed class SqliteAssessorStore(CasePlannerRepository repository) : IAssessorStore
{
    public string Provider => "Sqlite";
    public Task<List<AssessorRecord>> GetAsync(CancellationToken token = default) => repository.GetAssessorsAsync();
    public Task<AssessorRecord> SaveAsync(AssessorRecord model, CancellationToken token = default) => repository.SaveAssessorAsync(model);
}
