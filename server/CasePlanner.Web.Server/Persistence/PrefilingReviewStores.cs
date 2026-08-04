using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

public interface IPrefilingReviewStore
{
    string Provider { get; }
    Task<List<PrefilingReviewEventRecord>> GetEventsAsync(long? caseId, CancellationToken token = default);
    Task<PrefilingReviewEventRecord> RecordAsync(long caseId, PrefilingReviewActionRequest request, CancellationToken token = default);
    Task<PrefilingReviewEventRecord> RecordTitleReviewRoundAsync(long caseId, TitleReviewRoundRequest request, CancellationToken token = default);
    Task<PrefilingReviewSettings> GetSettingsAsync(CancellationToken token = default);
    Task<PrefilingReviewSettings> SaveSettingsAsync(SavePrefilingReviewSettingsRequest request, CancellationToken token = default);
}

public sealed class SqlitePrefilingReviewStore(CasePlannerRepository repository) : IPrefilingReviewStore
{
    public string Provider => "Sqlite";
    public Task<List<PrefilingReviewEventRecord>> GetEventsAsync(long? caseId, CancellationToken token = default) => repository.GetPrefilingReviewEventsAsync(caseId);
    public Task<PrefilingReviewEventRecord> RecordAsync(long caseId, PrefilingReviewActionRequest request, CancellationToken token = default) => repository.RecordPrefilingReviewActionAsync(caseId, request);
    public Task<PrefilingReviewEventRecord> RecordTitleReviewRoundAsync(long caseId, TitleReviewRoundRequest request, CancellationToken token = default) => repository.RecordTitleReviewRoundAsync(caseId, request);
    public Task<PrefilingReviewSettings> GetSettingsAsync(CancellationToken token = default) => repository.GetPrefilingReviewSettingsAsync();
    public Task<PrefilingReviewSettings> SaveSettingsAsync(SavePrefilingReviewSettingsRequest request, CancellationToken token = default) => repository.SavePrefilingReviewSettingsAsync(request);
}

