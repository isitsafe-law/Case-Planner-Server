using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

public interface IWitnessStore
{
    string Provider { get; }
    Task<List<WitnessRecord>> GetAsync(long? caseId, CancellationToken token = default);
    Task<WitnessRecord> SaveAsync(WitnessRecord model, CancellationToken token = default);
    Task DeleteAsync(long id, string? rowVersion = null, CancellationToken token = default);
}

public interface IExhibitStore
{
    string Provider { get; }
    Task<List<ExhibitRecord>> GetAsync(long? caseId, CancellationToken token = default);
    Task<ExhibitRecord> SaveAsync(ExhibitRecord model, CancellationToken token = default);
    Task DeleteAsync(long id, string? rowVersion = null, CancellationToken token = default);
}

public interface ITrialMotionStore
{
    string Provider { get; }
    Task<List<TrialMotionRecord>> GetAsync(long? caseId, CancellationToken token = default);
    Task<TrialMotionRecord> SaveAsync(TrialMotionRecord model, CancellationToken token = default);
    Task DeleteAsync(long id, string? rowVersion = null, CancellationToken token = default);
}

public sealed class SqliteWitnessStore(CasePlannerRepository repository) : IWitnessStore
{
    public string Provider => "Sqlite";
    public Task<List<WitnessRecord>> GetAsync(long? caseId, CancellationToken token = default) => repository.GetWitnessesAsync(caseId);
    public Task<WitnessRecord> SaveAsync(WitnessRecord model, CancellationToken token = default) => repository.SaveWitnessAsync(model);
    public Task DeleteAsync(long id, string? rowVersion = null, CancellationToken token = default) => repository.DeleteWitnessAsync(id);
}

public sealed class SqliteExhibitStore(CasePlannerRepository repository) : IExhibitStore
{
    public string Provider => "Sqlite";
    public Task<List<ExhibitRecord>> GetAsync(long? caseId, CancellationToken token = default) => repository.GetExhibitsAsync(caseId);
    public Task<ExhibitRecord> SaveAsync(ExhibitRecord model, CancellationToken token = default) => repository.SaveExhibitAsync(model);
    public Task DeleteAsync(long id, string? rowVersion = null, CancellationToken token = default) => repository.DeleteExhibitAsync(id);
}

public sealed class SqliteTrialMotionStore(CasePlannerRepository repository) : ITrialMotionStore
{
    public string Provider => "Sqlite";
    public Task<List<TrialMotionRecord>> GetAsync(long? caseId, CancellationToken token = default) => repository.GetTrialMotionsAsync(caseId);
    public Task<TrialMotionRecord> SaveAsync(TrialMotionRecord model, CancellationToken token = default) => repository.SaveTrialMotionAsync(model);
    public Task DeleteAsync(long id, string? rowVersion = null, CancellationToken token = default) => repository.DeleteTrialMotionAsync(id);
}
