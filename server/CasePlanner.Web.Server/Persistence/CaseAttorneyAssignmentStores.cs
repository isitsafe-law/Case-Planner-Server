using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

public interface ICaseAttorneyAssignmentStore
{
    string Provider { get; }
    Task<List<CaseAttorneyAssignmentRecord>> GetAsync(long? caseId, CancellationToken token = default);
    Task<CaseAttorneyAssignmentRecord> SaveAsync(CaseAttorneyAssignmentRecord model, CancellationToken token = default);
    Task DeleteAsync(long id, CancellationToken token = default);
}

public sealed class SqliteCaseAttorneyAssignmentStore(CasePlannerRepository repository) : ICaseAttorneyAssignmentStore
{
    public string Provider => "Sqlite";
    public Task<List<CaseAttorneyAssignmentRecord>> GetAsync(long? caseId, CancellationToken token = default) => repository.GetCaseAttorneyAssignmentsAsync(caseId);
    public Task<CaseAttorneyAssignmentRecord> SaveAsync(CaseAttorneyAssignmentRecord model, CancellationToken token = default) => repository.SaveCaseAttorneyAssignmentAsync(model);
    public Task DeleteAsync(long id, CancellationToken token = default) => repository.DeleteCaseAttorneyAssignmentAsync(id);
}

public sealed class SqlServerCaseAttorneyAssignmentStore : ICaseAttorneyAssignmentStore
{
    private const string Message = "Case attorney assignment SQL Server runtime is staged for the provider migration.";
    public string Provider => "SqlServer";
    public Task<List<CaseAttorneyAssignmentRecord>> GetAsync(long? caseId, CancellationToken token = default) => throw new NotSupportedException(Message);
    public Task<CaseAttorneyAssignmentRecord> SaveAsync(CaseAttorneyAssignmentRecord model, CancellationToken token = default) => throw new NotSupportedException(Message);
    public Task DeleteAsync(long id, CancellationToken token = default) => throw new NotSupportedException(Message);
}
