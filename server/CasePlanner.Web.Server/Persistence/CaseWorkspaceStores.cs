using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

public interface ICaseNoteStore
{
    string Provider { get; }
    Task<List<CaseNoteRecord>> GetAsync(long? caseId, CancellationToken token = default);
    Task<CaseNoteRecord> SaveAsync(CaseNoteRecord model, CancellationToken token = default);
    Task DeleteAsync(long id, string? rowVersion = null, CancellationToken token = default);
}

public interface IHearingStore
{
    string Provider { get; }
    Task<List<HearingRecord>> GetAsync(long? caseId, CancellationToken token = default);
    Task<HearingRecord> SaveAsync(HearingRecord model, CancellationToken token = default);
    Task DeleteAsync(long id, string? rowVersion = null, CancellationToken token = default);
}

public sealed class SqliteCaseNoteStore(CasePlannerRepository repository) : ICaseNoteStore
{
    public string Provider => "Sqlite";
    public Task<List<CaseNoteRecord>> GetAsync(long? caseId, CancellationToken token = default) => repository.GetCaseNotesAsync(caseId);
    public Task<CaseNoteRecord> SaveAsync(CaseNoteRecord model, CancellationToken token = default) => repository.SaveCaseNoteAsync(model);
    public Task DeleteAsync(long id, string? rowVersion = null, CancellationToken token = default) => repository.DeleteCaseNoteAsync(id);
}

public sealed class SqliteHearingStore(CasePlannerRepository repository) : IHearingStore
{
    public string Provider => "Sqlite";
    public Task<List<HearingRecord>> GetAsync(long? caseId, CancellationToken token = default) => repository.GetHearingsAsync(caseId);
    public Task<HearingRecord> SaveAsync(HearingRecord model, CancellationToken token = default) => repository.SaveHearingAsync(model);
    public Task DeleteAsync(long id, string? rowVersion = null, CancellationToken token = default) => repository.DeleteHearingAsync(id);
}
