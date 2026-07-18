using CasePlanner.Web.Server.Models;
using CasePlanner.Web.Server.Services;

namespace CasePlanner.Web.Server.Persistence;

public interface IRiskAnalysisService
{
    Task<RiskAnalysisResult> GetAsync(long caseId, CancellationToken token = default);
    Task<List<RiskAnalysisHistoryRecord>> GetHistoryAsync(long caseId, CancellationToken token = default);
    Task<RiskAnalysisResult> GetHistorySnapshotAsync(long caseId, long historyId, CancellationToken token = default);
    Task<RiskAnalysisResult> PreviewAsync(long caseId, RiskAnalysisInput input, CancellationToken token = default);
    Task<RiskAnalysisResult> SaveAsync(long caseId, RiskAnalysisInput input, CancellationToken token = default);
    Task DeleteAsync(long caseId, CancellationToken token = default);
    Task DeleteHistoryAsync(long caseId, long historyId, CancellationToken token = default);
    Task<List<RiskAnalysisOfferLogEntry>> GetOffersAsync(long caseId, CancellationToken token = default);
    Task<long?> GetOfferCaseIdAsync(long id, CancellationToken token = default);
    Task<RiskAnalysisOfferLogEntry> SaveOfferAsync(RiskAnalysisOfferLogEntry model, CancellationToken token = default);
    Task DeleteOfferAsync(long id, CancellationToken token = default);
}

public sealed class SqliteRiskAnalysisService(CasePlannerRepository repository) : IRiskAnalysisService
{
    public Task<RiskAnalysisResult> GetAsync(long caseId, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.GetRiskAnalysisAsync(caseId);
    }

    public Task<List<RiskAnalysisHistoryRecord>> GetHistoryAsync(long caseId, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.GetRiskAnalysisHistoryAsync(caseId);
    }

    public Task<RiskAnalysisResult> GetHistorySnapshotAsync(long caseId, long historyId, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.GetRiskAnalysisHistorySnapshotAsync(caseId, historyId);
    }

    public Task<RiskAnalysisResult> PreviewAsync(long caseId, RiskAnalysisInput input, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.PreviewRiskAnalysisAsync(caseId, input);
    }

    public Task<RiskAnalysisResult> SaveAsync(long caseId, RiskAnalysisInput input, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        input.CaseId = caseId;
        return repository.SaveRiskAnalysisAsync(input);
    }

    public Task DeleteAsync(long caseId, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.DeleteRiskAnalysisAsync(caseId);
    }

    public Task DeleteHistoryAsync(long caseId, long historyId, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.DeleteRiskAnalysisHistoryAsync(caseId, historyId);
    }

    public Task<List<RiskAnalysisOfferLogEntry>> GetOffersAsync(long caseId, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.GetOfferLogAsync(caseId);
    }

    public Task<long?> GetOfferCaseIdAsync(long id, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.GetChildCaseIdAsync("risk-offer", id);
    }

    public Task<RiskAnalysisOfferLogEntry> SaveOfferAsync(RiskAnalysisOfferLogEntry model, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.SaveOfferLogEntryAsync(model);
    }

    public Task DeleteOfferAsync(long id, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        return repository.DeleteOfferLogEntryAsync(id);
    }
}

public sealed class SqlServerRiskAnalysisService(
    SqlServerRiskAnalysisStore risk,
    SqlServerRiskOfferStore offers) : IRiskAnalysisService
{
    public Task<RiskAnalysisResult> GetAsync(long caseId, CancellationToken token = default) =>
        risk.GetAsync(caseId, token);

    public Task<List<RiskAnalysisHistoryRecord>> GetHistoryAsync(long caseId, CancellationToken token = default) =>
        risk.GetHistoryAsync(caseId, token);

    public Task<RiskAnalysisResult> GetHistorySnapshotAsync(long caseId, long historyId, CancellationToken token = default) =>
        risk.GetHistorySnapshotAsync(caseId, historyId, token);

    public Task<RiskAnalysisResult> PreviewAsync(long caseId, RiskAnalysisInput input, CancellationToken token = default) =>
        risk.PreviewAsync(caseId, input, token);

    public Task<RiskAnalysisResult> SaveAsync(long caseId, RiskAnalysisInput input, CancellationToken token = default)
    {
        input.CaseId = caseId;
        return risk.SaveAsync(input, token);
    }

    public async Task DeleteAsync(long caseId, CancellationToken token = default)
    {
        var current = await risk.GetAsync(caseId, token);
        if (current.Id != 0) await risk.DeleteAsync(current.Id, current.RowVersion, token);
    }

    public async Task DeleteHistoryAsync(long caseId, long historyId, CancellationToken token = default)
    {
        var snapshot = await risk.GetHistorySnapshotAsync(caseId, historyId, token);
        await risk.DeleteHistoryAsync(historyId, snapshot.RowVersion, token);
    }

    public Task<List<RiskAnalysisOfferLogEntry>> GetOffersAsync(long caseId, CancellationToken token = default) =>
        offers.GetAsync(caseId, token);

    public async Task<long?> GetOfferCaseIdAsync(long id, CancellationToken token = default) =>
        (await offers.GetAsync(null, token)).FirstOrDefault(x => x.Id == id)?.CaseId;

    public Task<RiskAnalysisOfferLogEntry> SaveOfferAsync(RiskAnalysisOfferLogEntry model, CancellationToken token = default) =>
        offers.SaveAsync(model, token);

    public async Task DeleteOfferAsync(long id, CancellationToken token = default)
    {
        var existing = (await offers.GetAsync(null, token)).FirstOrDefault(x => x.Id == id);
        if (existing is null) throw new InvalidOperationException("Risk analysis offer not found.");
        await offers.DeleteAsync(id, existing.RowVersion, token);
    }
}
