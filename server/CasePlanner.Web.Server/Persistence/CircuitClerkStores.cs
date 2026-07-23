using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

// Circuit Clerk reference lookup - same provider-switched shape as IAttorneyStore/ILegalAssistantStore
// (StaffDirectoryStores.cs): a fixed, independent reference table with zero auth/identity
// dependency. See CircuitClerkRecord for the full rationale.
public interface ICircuitClerkStore
{
    string Provider { get; }
    Task<List<CircuitClerkRecord>> GetAsync(CancellationToken token = default);
    Task<CircuitClerkRecord> SaveAsync(CircuitClerkRecord model, CancellationToken token = default);
}

public sealed class SqliteCircuitClerkStore(CasePlannerRepository repository) : ICircuitClerkStore
{
    public string Provider => "Sqlite";
    public Task<List<CircuitClerkRecord>> GetAsync(CancellationToken token = default) => repository.GetCircuitClerksAsync();
    public Task<CircuitClerkRecord> SaveAsync(CircuitClerkRecord model, CancellationToken token = default) => repository.SaveCircuitClerkAsync(model);
}
